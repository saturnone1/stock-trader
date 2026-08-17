using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Domain.MarketData;
using StockTrader.Services.Indicators;
using StockTrader.Services.ML;

namespace StockTrader.Services.Patterns;

public class PatternDetectionService
{
    private readonly IEnumerable<IPatternDetector> _detectors;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IPatternStatsRepository _statsRepo;
    private readonly ISignalScorer _signalScorer;
    private readonly IMarketRegimeClassifier _regimeClassifier;
    private readonly IIndicatorService _indicators;
    private readonly IOhlcvRepository _ohlcvRepository;
    private readonly AppDbContext _db;
    private readonly ILogger<PatternDetectionService> _logger;

    public PatternDetectionService(
        IEnumerable<IPatternDetector> detectors,
        ISettingsRepository settingsRepo,
        IPatternStatsRepository statsRepo,
        ISignalScorer signalScorer,
        IMarketRegimeClassifier regimeClassifier,
        IIndicatorService indicators,
        IOhlcvRepository ohlcvRepository,
        AppDbContext db,
        ILogger<PatternDetectionService> logger)
    {
        _detectors = detectors;
        _settingsRepo = settingsRepo;
        _statsRepo = statsRepo;
        _signalScorer = signalScorer;
        _regimeClassifier = regimeClassifier;
        _indicators = indicators;
        _ohlcvRepository = ohlcvRepository;
        _db = db;
        _logger = logger;
    }

    public async Task<List<PatternSignal>> ScanSymbolAsync(
        string symbol, OhlcvBar[] bars, MarketRegime regime, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetAsync(ct);

        // 종목별 활성 프로파일이 있으면 해당 프로파일의 패턴 목록 사용
        var profile = await _db.SymbolProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Symbol == symbol && p.IsActive, ct);

        var enabledPatterns = profile?.EnabledPatterns ?? settings.EnabledPatterns;

        if (profile != null)
        {
            _logger.LogDebug("종목 {Symbol}: 프로파일 '{Name}' 적용 (패턴 {Count}개)",
                symbol, profile.Name, enabledPatterns.Count);
        }

        // BUG-M04: MarketRegimeClassifier는 일봉 데이터(SPY 일봉)로 학습됨.
        // 분봉 bars를 그대로 넘기면 5/10/20일 수익률·변동성 피처가 왜곡(분 단위 변동)되어
        // 클러스터 할당이 무의미해진다. 분봉인 경우 ML 레짐 분류를 스킵하고
        // 호출자가 전달한 regime(일봉 기반으로 계산된 값)을 그대로 사용한다.
        var isIntraday = bars.Length > 0 && TimeFrameCatalog.IsIntraday(bars[0].TimeFrame);

        var effectiveRegime = (!isIntraday && _regimeClassifier.IsModelLoaded)
            ? await _regimeClassifier.ClassifyAsync(bars, ct)
            : regime;

        var signals = new List<PatternSignal>();

        foreach (var detector in _detectors
            .Where(d => enabledPatterns.Contains(d.PatternType)))
        {
            try
            {
                var signal = await detector.DetectAsync(symbol, bars, effectiveRegime, ct);
                if (signal != null)
                {
                    // ML 기반 신뢰도 보정
                    signal.Confidence = await EnhanceConfidenceAsync(signal, bars, effectiveRegime, ct);

                    _logger.LogInformation(
                        "Pattern {Pattern} detected for {Symbol} (confidence={Confidence:F2})",
                        detector.PatternType, symbol, signal.Confidence);

                    signals.Add(signal);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting pattern {Pattern} for {Symbol}",
                    detector.PatternType, symbol);
            }
        }

        var activeCustomPatterns = await _db.CustomPatterns
            .AsNoTracking()
            .Where(pattern => pattern.IsActive && pattern.EnableLiveTrading)
            .OrderBy(pattern => pattern.Id)
            .ToListAsync(ct);

        foreach (var definition in activeCustomPatterns)
        {
            try
            {
                if (CustomPatternValidator.Validate(definition).Count > 0) continue;
                if (bars.Length == 0 || definition.TimeFrame != bars[^1].TimeFrame)
                    continue;
                var detector = new RuleBasedDetector(_indicators, definition);
                var referenceData = await LoadReferenceDataAsync(definition, symbol, bars, ct);
                detector.SetReferenceData(referenceData, bars[^1].Timestamp);
                var signal = await detector.DetectAsync(symbol, bars, effectiveRegime, ct);
                if (signal == null) continue;
                signal.Confidence = await EnhanceConfidenceAsync(signal, bars, effectiveRegime, ct);
                signals.Add(signal);
                _logger.LogInformation("Custom strategy {Strategy} detected for {Symbol}", definition.Name, symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting custom strategy {Strategy} for {Symbol}", definition.Name, symbol);
            }
        }

        return signals;
    }

    private async Task<Dictionary<string, OhlcvBar[]>> LoadReferenceDataAsync(
        CustomPatternDefinition definition, string symbol, OhlcvBar[] bars, CancellationToken ct)
    {
        var result = new Dictionary<string, OhlcvBar[]>(StringComparer.OrdinalIgnoreCase)
        {
            [symbol] = bars
        };
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRules(IEnumerable<EntryRule> rules)
        {
            foreach (var rule in rules)
                if (!string.IsNullOrWhiteSpace(rule.RefSymbol)) symbols.Add(rule.RefSymbol.Trim().ToUpperInvariant());
        }

        void AddGroups(string json)
        {
            foreach (var group in JsonSerializer.Deserialize<List<ConditionGroup>>(json, options) ?? []) AddRules(group.Rules);
        }

        AddRules(JsonSerializer.Deserialize<List<EntryRule>>(definition.EntryRulesJson, options) ?? []);
        AddRules(JsonSerializer.Deserialize<List<EntryRule>>(definition.ExitRulesJson, options) ?? []);
        AddGroups(definition.EntryGroupsJson);
        AddGroups(definition.ExitGroupsJson);
        foreach (var tier in JsonSerializer.Deserialize<List<WeightTier>>(definition.WeightTiersJson, options) ?? []) AddRules(tier.Conditions);
        foreach (var scaling in JsonSerializer.Deserialize<List<ScalingRule>>(definition.ScalingRulesJson, options) ?? []) AddRules(scaling.Conditions);

        var timeFrame = bars[0].TimeFrame;
        foreach (var referenceSymbol in symbols.Where(value => !value.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
        {
            result[referenceSymbol] = (await _ohlcvRepository.GetBarsAsync(
                    referenceSymbol, timeFrame, bars[0].Timestamp, bars[^1].Timestamp.AddDays(1), ct))
                .OrderBy(bar => bar.Timestamp)
                .ToArray();
        }
        return result;
    }

    /// <summary>
    /// 해당 패턴의 역사적 승률을 조회 후 ML SignalScorer로 Confidence를 보정합니다.
    /// 모델이 없으면 원래 값을 그대로 반환합니다.
    /// </summary>
    private async Task<decimal> EnhanceConfidenceAsync(
        PatternSignal signal, OhlcvBar[] bars, MarketRegime regime, CancellationToken ct)
    {
        if (!_signalScorer.IsModelLoaded)
            return signal.Confidence;

        try
        {
            // 역사적 승률 조회 (없으면 0.5 기본값)
            var stats = string.IsNullOrWhiteSpace(signal.CustomPatternName)
                ? await _statsRepo.GetAsync(signal.PatternType, symbol: signal.Symbol, ct)
                : null;
            var winRate = stats?.WinRate ?? 0.5m;

            return await _signalScorer.ScoreAsync(signal, bars, regime, winRate, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ML 신뢰도 보정 실패 — 기존 값 사용");
            return signal.Confidence;
        }
    }
}

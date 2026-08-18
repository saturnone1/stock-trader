using StockTrader.Application.MachineLearning;
using StockTrader.Application.Strategies;
using StockTrader.Application.Settings;
using StockTrader.Application.Statistics;
using StockTrader.Application.SymbolProfiles;
using StockTrader.Application.Trading;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Domain.MarketData;
using StockTrader.Services.Indicators;
using StockTrader.Services.Backtest;
using Microsoft.Extensions.Options;

namespace StockTrader.Services.Patterns;

public class PatternDetectionService : ILivePatternDetection
{
    private readonly IBuiltInPatternDetectorFactory _builtInDetectors;
    private readonly ILiveParameterService _liveParameters;
    private readonly PatternSettings _basePatternSettings;
    private readonly IPatternStatisticsQuery _patternStatistics;
    private readonly ISignalScorer _signalScorer;
    private readonly IMarketRegimeClassifier _regimeClassifier;
    private readonly ICustomStrategyDetectorFactory _customDetectors;
    private readonly IOhlcvRepository _ohlcvRepository;
    private readonly ICompiledStrategyRepository _strategies;
    private readonly SymbolProfileManagementService _symbolProfiles;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PatternDetectionService> _logger;

    public PatternDetectionService(
        IBuiltInPatternDetectorFactory builtInDetectors,
        ILiveParameterService liveParameters,
        IOptions<PatternSettings> patternSettings,
        IPatternStatisticsQuery patternStatistics,
        ISignalScorer signalScorer,
        IMarketRegimeClassifier regimeClassifier,
        ICustomStrategyDetectorFactory customDetectors,
        IOhlcvRepository ohlcvRepository,
        ICompiledStrategyRepository strategies,
        SymbolProfileManagementService symbolProfiles,
        TimeProvider timeProvider,
        ILogger<PatternDetectionService> logger)
    {
        _builtInDetectors = builtInDetectors;
        _liveParameters = liveParameters;
        _basePatternSettings = patternSettings.Value;
        _patternStatistics = patternStatistics;
        _signalScorer = signalScorer;
        _regimeClassifier = regimeClassifier;
        _customDetectors = customDetectors;
        _ohlcvRepository = ohlcvRepository;
        _strategies = strategies;
        _symbolProfiles = symbolProfiles;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<List<PatternSignal>> ScanSymbolAsync(
        string symbol, OhlcvBar[] bars, MarketRegime regime, CancellationToken ct = default)
    {
        symbol = MarketSymbolPolicy.Normalize(symbol);
        var liveConfiguration = await _liveParameters.GetAsync(ct);
        var patternSettings = liveConfiguration.Overrides is null
            ? _basePatternSettings
            : PatternOverrideMerger.Merge(_basePatternSettings, liveConfiguration.Overrides);
        var detectors = _builtInDetectors.CreateAll(patternSettings);

        // 종목별 활성 프로파일이 있으면 해당 프로파일의 패턴 목록 사용
        var profile = await _symbolProfiles.GetActiveAsync(symbol, ct);

        var enabledPatterns = profile?.EnabledPatterns ?? liveConfiguration.EnabledPatterns;

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

        foreach (var detector in detectors
            .Where(d => enabledPatterns.Contains(d.PatternType)))
        {
            try
            {
                var signal = await detector.DetectAsync(symbol, bars, effectiveRegime, ct);
                if (signal != null)
                {
                    StampLiveTiming(signal, bars);
                    ApplyScoringResult(
                        signal,
                        await EvaluateConfidenceAsync(signal, bars, effectiveRegime, ct));

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

        var activeCustomPatterns = await _strategies.ListAsync(activeOnly: true, liveOnly: true, ct);

        foreach (var strategy in activeCustomPatterns)
        {
            try
            {
                if (bars.Length == 0 || strategy.TimeFrame != bars[^1].TimeFrame)
                    continue;
                var detector = _customDetectors.Create(strategy);
                var referenceData = await LoadReferenceDataAsync(strategy, symbol, bars, ct);
                detector.SetReferenceData(referenceData, bars[^1].Timestamp);
                var signal = await detector.DetectAsync(symbol, bars, effectiveRegime, ct);
                if (signal == null) continue;
                StampLiveTiming(signal, bars);
                ApplyScoringResult(
                    signal,
                    await EvaluateConfidenceAsync(signal, bars, effectiveRegime, ct));
                signals.Add(signal);
                _logger.LogInformation("Custom strategy {Strategy} detected for {Symbol}", strategy.Name, symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting custom strategy {Strategy} for {Symbol}", strategy.Name, symbol);
            }
        }

        return signals;
    }

    private void StampLiveTiming(PatternSignal signal, OhlcvBar[] bars)
    {
        signal.SignalBarAt = bars[^1].Timestamp;
        signal.DetectedAt = _timeProvider.GetUtcNow().UtcDateTime;
    }

    private async Task<Dictionary<string, OhlcvBar[]>> LoadReferenceDataAsync(
        CompiledStrategy strategy, string symbol, OhlcvBar[] bars, CancellationToken ct)
    {
        var result = new Dictionary<string, OhlcvBar[]>(StringComparer.OrdinalIgnoreCase)
        {
            [symbol] = bars
        };
        var timeFrame = bars[0].TimeFrame;
        foreach (var referenceSymbol in strategy.ReferenceSymbols.Where(value => !value.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
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
    private async Task<SignalScoringResult> EvaluateConfidenceAsync(
        PatternSignal signal, OhlcvBar[] bars, MarketRegime regime, CancellationToken ct)
    {
        try
        {
            // 종목별 역사적 승률을 우선하고, 없으면 패턴 전체 통계를 사용합니다.
            var stats = string.IsNullOrWhiteSpace(signal.CustomPatternName)
                ? PatternStatisticsSelectionPolicy.Resolve(
                    signal.PatternType,
                    signal.Symbol,
                    await _patternStatistics.GetAllAsync(ct))
                : null;
            var winRate = stats?.WinRate ?? 0.5m;

            return await _signalScorer.EvaluateAsync(signal, bars, regime, winRate, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ML 신뢰도 보정 실패 — 기존 값 사용");
            return new SignalScoringResult(signal.Confidence, null);
        }
    }

    private static void ApplyScoringResult(
        PatternSignal signal,
        SignalScoringResult result)
    {
        signal.Confidence = result.Confidence;
        if (result.Features is not { } features) return;

        signal.ScoringFeatureVersion = features.SchemaVersion;
        signal.ScoringRsi = features.Rsi;
        signal.ScoringBollingerPosition = features.BollingerPosition;
        signal.ScoringVolumeRatio = features.VolumeRatio;
        signal.ScoringMarketRegimeCode = features.MarketRegimeCode;
        signal.ScoringAtrPercent = features.AtrPercent;
        signal.ScoringHistoricalWinRate = features.HistoricalWinRate;
        signal.ScoringRiskRewardRatio = features.RiskRewardRatio;
        signal.ScoringPriceVsLongMovingAverage = features.PriceVsLongMovingAverage;
        signal.ScoringLongTrendHistoryAvailable = features.LongTrendHistoryAvailable;
    }
}

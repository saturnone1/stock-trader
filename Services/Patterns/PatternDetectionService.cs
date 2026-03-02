using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.ML;

namespace StockTrader.Services.Patterns;

public class PatternDetectionService
{
    private readonly IEnumerable<IPatternDetector> _detectors;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IPatternStatsRepository _statsRepo;
    private readonly ISignalScorer _signalScorer;
    private readonly IMarketRegimeClassifier _regimeClassifier;
    private readonly ILogger<PatternDetectionService> _logger;

    public PatternDetectionService(
        IEnumerable<IPatternDetector> detectors,
        ISettingsRepository settingsRepo,
        IPatternStatsRepository statsRepo,
        ISignalScorer signalScorer,
        IMarketRegimeClassifier regimeClassifier,
        ILogger<PatternDetectionService> logger)
    {
        _detectors = detectors;
        _settingsRepo = settingsRepo;
        _statsRepo = statsRepo;
        _signalScorer = signalScorer;
        _regimeClassifier = regimeClassifier;
        _logger = logger;
    }

    public async Task<List<PatternSignal>> ScanSymbolAsync(
        string symbol, OhlcvBar[] bars, MarketRegime regime, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetAsync(ct);

        // ML 레짐 분류기는 일봉 데이터로 학습됨.
        // 분봉 bars를 그대로 넘기면 수익률/변동성 피처가 왜곡되므로 분봉인 경우 스킵한다.
        var isIntraday = bars.Length > 0 && bars[0].TimeFrame is
            TimeFrame.OneMinute or TimeFrame.FiveMinute or TimeFrame.FifteenMinute;

        var effectiveRegime = (!isIntraday && _regimeClassifier.IsModelLoaded)
            ? await _regimeClassifier.ClassifyAsync(bars, ct)
            : regime;

        var signals = new List<PatternSignal>();

        foreach (var detector in _detectors
            .Where(d => settings.EnabledPatterns.Contains(d.PatternType)))
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

        return signals;
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
            var stats = await _statsRepo.GetAsync(signal.PatternType, symbol: signal.Symbol, ct);
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

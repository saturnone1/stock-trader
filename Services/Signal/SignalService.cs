using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Services.Risk;
using StockTrader.Services.Statistics;

namespace StockTrader.Services.Signal;

public class SignalService : ISignalService
{
    private readonly IStatisticsService _statsService;
    private readonly IRiskManagementService _riskService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly AppDbContext _db;
    private readonly TradingSettings _tradingSettings;
    private readonly ILogger<SignalService> _logger;

    public SignalService(
        IStatisticsService statsService,
        IRiskManagementService riskService,
        ISettingsRepository settingsRepo,
        AppDbContext db,
        IOptions<TradingSettings> tradingSettings,
        ILogger<SignalService> logger)
    {
        _statsService = statsService;
        _riskService = riskService;
        _settingsRepo = settingsRepo;
        _db = db;
        _tradingSettings = tradingSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// 거래 이력이 부족한 패턴의 최소 샘플 수.
    /// 이 수 미만이면 Expectancy 필터를 우회하여 신규 패턴도 거래 가능.
    /// </summary>
    private const int MinSamplesForExpectancyFilter = 10;

    public async Task<List<TradeRecommendation>> EvaluateSignalsAsync(
        List<PatternSignal> signals, CancellationToken ct = default)
    {
        var recommendations = new List<TradeRecommendation>();
        var settings = await _settingsRepo.GetAsync(ct);

        // 섹터 정보를 일괄 조회하여 N+1 방지 (W02 fix)
        var symbols = signals.Select(s => s.Symbol).Distinct().ToList();
        var sectorMap = await _db.Tickers
            .Where(t => symbols.Contains(t.Symbol))
            .ToDictionaryAsync(t => t.Symbol, t => t.Sector, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var signal in signals)
        {
            var stats = await _statsService.GetStatsAsync(signal.PatternType, ct: ct);

            // Gap 3 fix: 거래 이력이 충분한 경우에만 Expectancy 필터 적용.
            // 신규 패턴(stats==null 또는 샘플 부족)은 통과시켜서 거래 기회 확보.
            if (stats != null
                && stats.SampleSize >= MinSamplesForExpectancyFilter
                && stats.Expectancy <= _tradingSettings.MinExpectancy)
            {
                _logger.LogDebug(
                    "Signal {Pattern} for {Symbol} filtered: expectancy {Actual:F4} <= min {Min:F4} (samples={Samples})",
                    signal.PatternType, signal.Symbol,
                    stats.Expectancy, _tradingSettings.MinExpectancy, stats.SampleSize);
                continue;
            }

            // W02 fix: Tickers 테이블에서 섹터 조회; 없으면 심볼 자체를 섹터로 사용하여
            // 동일 종목 중복 포지션을 MaxPositionsPerSector 체크가 잡아낼 수 있도록 함.
            var sector = sectorMap.TryGetValue(signal.Symbol, out var s) && !string.IsNullOrEmpty(s)
                ? s
                : signal.Symbol;

            var (allowed, reason) = await _riskService.CanOpenPositionAsync(
                signal.Symbol, sector, ct);

            if (!allowed)
            {
                _logger.LogInformation("Signal {Pattern} for {Symbol} blocked by risk: {Reason}",
                    signal.PatternType, signal.Symbol, reason);
                continue;
            }

            var positionSize = _riskService.CalculatePositionSize(
                settings.AccountSize,
                _tradingSettings.RiskPerTradePercent,
                signal.EntryPrice,
                signal.StopLossPrice);

            // Gap 4 fix: 백테스트와 동일하게 1/MaxTotalPositions 캡 적용
            var maxTotalPositions = _tradingSettings.MaxTotalPositions;
            if (maxTotalPositions > 0 && signal.EntryPrice > 0)
            {
                var maxPositionCapitalRatio = 1.0m / maxTotalPositions;
                var maxPositionSize = settings.AccountSize * maxPositionCapitalRatio;
                positionSize = Math.Min(positionSize, maxPositionSize);
            }

            var shareQty = signal.EntryPrice > 0
                ? (int)Math.Floor(positionSize / signal.EntryPrice)
                : 0;

            var recommendation = new TradeRecommendation
            {
                Symbol = signal.Symbol,
                PatternType = signal.PatternType,
                GeneratedAt = DateTime.UtcNow,
                EntryPrice = signal.EntryPrice,
                StopLossPrice = signal.StopLossPrice,
                TargetPrice = signal.TargetPrice,
                PositionSize = positionSize,
                ShareQuantity = shareQty,
                Expectancy = stats?.Expectancy ?? 0m,
                WasExecuted = false,
                Mode = settings.OrderMode
            };

            recommendations.Add(recommendation);
            _logger.LogInformation(
                "Recommendation: {Pattern} {Symbol} Entry={Entry} SL={SL} Target={Target} Qty={Qty}",
                signal.PatternType, signal.Symbol, signal.EntryPrice,
                signal.StopLossPrice, signal.TargetPrice, shareQty);
        }

        _logger.LogInformation(
            "Signal evaluation complete: {Input} signals → {Output} recommendations (filtered: {Filtered})",
            signals.Count, recommendations.Count, signals.Count - recommendations.Count);

        return recommendations;
    }
}

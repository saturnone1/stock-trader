using Microsoft.Extensions.Options;
using StockTrader.Configuration;
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
    private readonly TradingSettings _tradingSettings;
    private readonly ILogger<SignalService> _logger;

    public SignalService(
        IStatisticsService statsService,
        IRiskManagementService riskService,
        ISettingsRepository settingsRepo,
        IOptions<TradingSettings> tradingSettings,
        ILogger<SignalService> logger)
    {
        _statsService = statsService;
        _riskService = riskService;
        _settingsRepo = settingsRepo;
        _tradingSettings = tradingSettings.Value;
        _logger = logger;
    }

    public async Task<List<TradeRecommendation>> EvaluateSignalsAsync(
        List<PatternSignal> signals, CancellationToken ct = default)
    {
        var recommendations = new List<TradeRecommendation>();
        var settings = await _settingsRepo.GetAsync(ct);

        foreach (var signal in signals)
        {
            var stats = await _statsService.GetStatsAsync(signal.PatternType, ct: ct);

            if (stats == null || stats.Expectancy <= _tradingSettings.MinExpectancy)
            {
                _logger.LogDebug("Signal {Pattern} for {Symbol} filtered: insufficient expectancy",
                    signal.PatternType, signal.Symbol);
                continue;
            }

            var (allowed, reason) = await _riskService.CanOpenPositionAsync(
                signal.Symbol, "", ct);

            if (!allowed)
            {
                _logger.LogInformation("Signal {Pattern} for {Symbol} blocked by risk: {Reason}",
                    signal.PatternType, signal.Symbol, reason);
                continue;
            }

            var stopLossPercent = signal.EntryPrice != 0
                ? Math.Abs(signal.EntryPrice - signal.StopLossPrice) / signal.EntryPrice
                : 0.02m;

            var positionSize = _riskService.CalculatePositionSize(
                settings.AccountSize,
                _tradingSettings.RiskPerTradePercent,
                signal.EntryPrice,
                signal.StopLossPrice);

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
                Expectancy = stats.Expectancy,
                WasExecuted = false,
                Mode = settings.OrderMode
            };

            recommendations.Add(recommendation);
            _logger.LogInformation(
                "Recommendation: {Pattern} {Symbol} Entry={Entry} SL={SL} Target={Target} Qty={Qty}",
                signal.PatternType, signal.Symbol, signal.EntryPrice,
                signal.StopLossPrice, signal.TargetPrice, shareQty);
        }

        return recommendations;
    }
}

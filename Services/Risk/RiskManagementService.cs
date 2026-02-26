using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Services.Risk;

public class RiskManagementService : IRiskManagementService
{
    private readonly ITradeRepository _tradeRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly TradingSettings _tradingSettings;
    private readonly ILogger<RiskManagementService> _logger;

    private RiskState _currentState = new();

    public RiskManagementService(
        ITradeRepository tradeRepo,
        ISettingsRepository settingsRepo,
        IOptions<TradingSettings> tradingSettings,
        ILogger<RiskManagementService> logger)
    {
        _tradeRepo = tradeRepo;
        _settingsRepo = settingsRepo;
        _tradingSettings = tradingSettings.Value;
        _logger = logger;
    }

    public Task<RiskState> GetCurrentRiskStateAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_currentState);
    }

    public async Task<(bool Allowed, string Reason)> CanOpenPositionAsync(
        string symbol, string sector, CancellationToken ct = default)
    {
        if (_currentState.IsTradingHalted)
            return (false, "Trading halted: daily loss limit reached");

        var openPositions = await _tradeRepo.GetOpenPositionsAsync(ct);

        if (openPositions.Count >= _tradingSettings.MaxTotalPositions)
            return (false, $"Max total positions ({_tradingSettings.MaxTotalPositions}) reached");

        if (openPositions.Any(p => p.Symbol == symbol))
            return (false, $"Already have open position in {symbol}");

        if (!string.IsNullOrEmpty(sector))
        {
            var sectorCount = openPositions.Count(p => p.Sector == sector);
            if (sectorCount >= _tradingSettings.MaxPositionsPerSector)
                return (false, $"Max positions per sector ({_tradingSettings.MaxPositionsPerSector}) reached for {sector}");
        }

        return (true, string.Empty);
    }

    public decimal CalculatePositionSize(decimal accountSize, decimal riskPercent,
        decimal entryPrice, decimal stopLossPrice)
    {
        if (entryPrice == 0 || stopLossPrice == 0) return 0;

        var stopLossPercent = Math.Abs(entryPrice - stopLossPrice) / entryPrice;
        if (stopLossPercent == 0) return 0;

        return accountSize * riskPercent / stopLossPercent;
    }

    public async Task UpdateDailyPnLAsync(CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetAsync(ct);
        var openPositions = await _tradeRepo.GetOpenPositionsAsync(ct);

        var dailyPnL = openPositions.Sum(p => p.UnrealizedPnL);
        var dailyPnLPercent = settings.AccountSize > 0
            ? dailyPnL / settings.AccountSize
            : 0;

        var sectorCounts = openPositions
            .GroupBy(p => p.Sector)
            .ToDictionary(g => g.Key, g => g.Count());

        _currentState = new RiskState
        {
            DailyPnL = dailyPnL,
            DailyPnLPercent = dailyPnLPercent,
            IsTradingHalted = dailyPnLPercent <= -_tradingSettings.DailyLossLimitPercent,
            OpenPositionCount = openPositions.Count,
            PositionsPerSector = sectorCounts,
            LastUpdated = DateTime.UtcNow
        };

        if (_currentState.IsTradingHalted)
        {
            _logger.LogWarning("TRADING HALTED: Daily loss {PnL:P2} exceeds limit {Limit:P2}",
                dailyPnLPercent, -_tradingSettings.DailyLossLimitPercent);
        }
    }
}

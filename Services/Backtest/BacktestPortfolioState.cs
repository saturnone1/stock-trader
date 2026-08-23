using StockTrader.Application.Backtesting;
using StockTrader.Engine.Portfolio;
using StockTrader.Models;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 백테스트 포트폴리오의 실현 자본, 보유 포지션, 일중 손실 기준과 시가평가 곡선을 관리합니다.
/// </summary>
internal sealed class BacktestPortfolioState(decimal initialCapital, DateTime startedAt)
{
    private readonly PortfolioAccountingLedger _accounting = new(initialCapital, startedAt);

    public Dictionary<string, BacktestExecutionAdapter.OpenPosition> OpenPositions { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<EquityPoint> EquityCurve => _accounting.EquityCurve
        .Select(point => new EquityPoint(point.Timestamp, point.Equity))
        .ToList();
    public decimal CurrentEquity => _accounting.CurrentEquity;
    public decimal MaxDrawdown => _accounting.MaxDrawdown;
    public int WeightReducedTrades { get; private set; }

    public void ApplyRealizedTrade(TradeRecord trade) => _accounting.ApplyRealizedPnl(trade.PnL);

    public void UpdateLatestPrices(
        DateTime timestamp,
        IReadOnlyDictionary<string, PreparedSymbolData> symbolData)
    {
        foreach (var (symbol, data) in symbolData)
        {
            if (data.TimestampToIndex.TryGetValue(timestamp, out var barIndex))
                _accounting.ObservePrice(symbol, data.Bars[barIndex].Close);
        }
    }

    public void BeginTradingDay(DateOnly tradingDay) => _accounting.BeginTradingDay(tradingDay);

    public bool HasReachedDailyLossLimit(decimal dailyLossLimitPercent) =>
        _accounting.HasReachedDailyLossLimit(dailyLossLimitPercent);

    public void RecordMarkedEquity(DateTime timestamp)
    {
        _accounting.RecordMarkedEquity(timestamp, OpenPositions.Select(pair =>
            new PositionMark(
                pair.Key,
                pair.Value.EntryPrice,
                pair.Value.CurrentQuantity > 0
                    ? pair.Value.CurrentQuantity
                    : pair.Value.Quantity)));
    }

    public void RegisterWeightReductions(int count = 1) => WeightReducedTrades += Math.Max(0, count);
}

using StockTrader.Application.Backtesting;
using StockTrader.Models;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 백테스트 포트폴리오의 실현 자본, 보유 포지션, 일중 손실 기준과 시가평가 곡선을 관리합니다.
/// </summary>
internal sealed class BacktestPortfolioState(decimal initialCapital, DateTime startedAt)
{
    private readonly Dictionary<string, decimal> _latestPrices =
        new(StringComparer.OrdinalIgnoreCase);
    private decimal _dailyStartEquity = initialCapital;
    private DateOnly _dailyLossDate = DateOnly.MinValue;
    private decimal _peakMarkedEquity = initialCapital;

    public Dictionary<string, BacktestExecutionAdapter.OpenPosition> OpenPositions { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<EquityPoint> EquityCurve { get; } = [new(startedAt, initialCapital)];
    public decimal CurrentEquity { get; private set; } = initialCapital;
    public decimal MaxDrawdown { get; private set; }
    public int WeightReducedTrades { get; private set; }

    public void ApplyRealizedTrade(TradeRecord trade) => CurrentEquity += trade.PnL;

    public void UpdateLatestPrices(
        DateTime timestamp,
        IReadOnlyDictionary<string, PreparedSymbolData> symbolData)
    {
        foreach (var (symbol, data) in symbolData)
        {
            if (data.TimestampToIndex.TryGetValue(timestamp, out var barIndex))
                _latestPrices[symbol] = data.Bars[barIndex].Close;
        }
    }

    public void BeginTradingDay(DateOnly tradingDay)
    {
        if (tradingDay == _dailyLossDate) return;

        _dailyLossDate = tradingDay;
        _dailyStartEquity = CurrentEquity;
    }

    public bool HasReachedDailyLossLimit(decimal dailyLossLimitPercent) =>
        dailyLossLimitPercent > 0
        && _dailyStartEquity > 0
        && CurrentEquity <= _dailyStartEquity * (1 - dailyLossLimitPercent);

    public void RecordMarkedEquity(DateTime timestamp)
    {
        var unrealizedPnl = OpenPositions.Sum(pair =>
            _latestPrices.TryGetValue(pair.Key, out var price)
                ? (price - pair.Value.EntryPrice)
                    * (pair.Value.CurrentQuantity > 0
                        ? pair.Value.CurrentQuantity
                        : pair.Value.Quantity)
                : 0m);
        var markedEquity = CurrentEquity + unrealizedPnl;
        if (markedEquity > _peakMarkedEquity) _peakMarkedEquity = markedEquity;

        var drawdown = _peakMarkedEquity > 0
            ? (_peakMarkedEquity - markedEquity) / _peakMarkedEquity
            : 0m;
        if (drawdown > MaxDrawdown) MaxDrawdown = drawdown;

        if (EquityCurve.Count > 0 && EquityCurve[^1].Date == timestamp)
            EquityCurve[^1] = new EquityPoint(timestamp, markedEquity);
        else
            EquityCurve.Add(new EquityPoint(timestamp, markedEquity));
    }

    public void RegisterWeightReduction() => WeightReducedTrades++;
}

namespace StockTrader.Engine.Portfolio;

public readonly record struct PositionMark(
    string Symbol,
    decimal EntryPrice,
    int Quantity);

public readonly record struct PortfolioEquityPoint(
    DateTime Timestamp,
    decimal Equity);

/// <summary>
/// 저장소나 거래 모델에 의존하지 않고 실현 손익, 일 손실 한도, 시가평가 자본과 낙폭을
/// 한 순서로 계산합니다.
/// </summary>
public sealed class PortfolioAccountingLedger(decimal initialCapital, DateTime startedAt)
{
    private readonly Dictionary<string, decimal> _latestPrices =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PortfolioEquityPoint> _equityCurve =
        [new(startedAt, initialCapital)];
    private decimal _dailyStartEquity = initialCapital;
    private DateOnly _dailyLossDate = DateOnly.MinValue;
    private decimal _peakMarkedEquity = initialCapital;

    public decimal CurrentEquity { get; private set; } = initialCapital;
    public decimal MaxDrawdown { get; private set; }
    public IReadOnlyList<PortfolioEquityPoint> EquityCurve => _equityCurve;

    public void ApplyRealizedPnl(decimal pnl) => CurrentEquity += pnl;

    public void ObservePrice(string symbol, decimal price) => _latestPrices[symbol] = price;

    public void BeginTradingDay(DateOnly tradingDay)
    {
        if (tradingDay == _dailyLossDate) return;

        _dailyLossDate = tradingDay;
        _dailyStartEquity = CurrentEquity;
    }

    public bool HasReachedDailyLossLimit(decimal dailyLossLimitFraction) =>
        dailyLossLimitFraction > 0
        && _dailyStartEquity > 0
        && CurrentEquity <= _dailyStartEquity * (1 - dailyLossLimitFraction);

    public PortfolioEquityPoint RecordMarkedEquity(
        DateTime timestamp,
        IEnumerable<PositionMark> openPositions)
    {
        var unrealizedPnl = openPositions.Sum(position =>
            _latestPrices.TryGetValue(position.Symbol, out var price)
                ? (price - position.EntryPrice) * Math.Max(0, position.Quantity)
                : 0m);
        var markedEquity = CurrentEquity + unrealizedPnl;
        if (markedEquity > _peakMarkedEquity) _peakMarkedEquity = markedEquity;

        var drawdown = _peakMarkedEquity > 0
            ? (_peakMarkedEquity - markedEquity) / _peakMarkedEquity
            : 0m;
        if (drawdown > MaxDrawdown) MaxDrawdown = drawdown;

        var point = new PortfolioEquityPoint(timestamp, markedEquity);
        if (_equityCurve.Count > 0 && _equityCurve[^1].Timestamp == timestamp)
            _equityCurve[^1] = point;
        else
            _equityCurve.Add(point);
        return point;
    }
}

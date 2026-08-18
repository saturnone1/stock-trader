using StockTrader.Domain.Strategies;

namespace StockTrader.Application.Signals;

public sealed record SignalBrowseRequest(
    string? Pattern,
    string? Search,
    string? Sort,
    string? Style);

public sealed record BrowsableSignal(
    long Id,
    string Symbol,
    PatternType PatternType,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    decimal Confidence,
    string Details,
    DateTime DetectedAt);

public sealed record SignalListItem(
    long Id,
    string Symbol,
    string Pattern,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    decimal Confidence,
    decimal RiskReward,
    string Details,
    DateTime DetectedAt,
    decimal? PatternWinRate,
    decimal? PatternExpectancy);

public sealed record SignalListSnapshot(IReadOnlyList<SignalListItem> Signals)
{
    public int Count => Signals.Count;
}

public interface ISignalListQuery
{
    Task<SignalListSnapshot> GetAsync(
        SignalBrowseRequest request,
        CancellationToken ct = default);
}

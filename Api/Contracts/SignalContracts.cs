using StockTrader.Application.Signals;

namespace StockTrader.Api.Contracts;

public sealed record SignalListItemResponse(
    long Id,
    string Symbol,
    string Pattern,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    decimal Confidence,
    decimal RiskReward,
    string Details,
    string DetectedAt,
    decimal? PatternWinRate,
    decimal? PatternExpectancy);

public sealed record SignalListResponse(
    int Count,
    IReadOnlyList<SignalListItemResponse> Signals)
{
    public static SignalListResponse Create(SignalListSnapshot snapshot) => new(
        snapshot.Count,
        snapshot.Signals.Select(signal => new SignalListItemResponse(
            signal.Id,
            signal.Symbol,
            signal.Pattern,
            signal.EntryPrice,
            signal.StopLossPrice,
            signal.TargetPrice,
            signal.Confidence,
            signal.RiskReward,
            signal.Details,
            signal.DetectedAt.ToString("o"),
            signal.PatternWinRate,
            signal.PatternExpectancy)).ToArray());
}

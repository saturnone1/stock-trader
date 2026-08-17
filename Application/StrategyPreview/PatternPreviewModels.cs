using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.StrategyPreview;

public sealed record PatternPreviewQuery(
    string Symbol,
    CustomPatternDefinition Pattern,
    TimeFrame TimeFrame,
    DateTime? From,
    DateTime? To);

public enum PatternPreviewOutcomeKind
{
    Success,
    InvalidRequest,
    ProviderUnavailable,
    DataNotFound
}

public sealed record PatternPreviewOutcome(
    PatternPreviewOutcomeKind Kind,
    PatternPreviewResult? Result = null,
    string? Error = null,
    IReadOnlyList<string>? Errors = null)
{
    public static PatternPreviewOutcome Success(PatternPreviewResult result) =>
        new(PatternPreviewOutcomeKind.Success, result);

    public static PatternPreviewOutcome Invalid(string error, IReadOnlyList<string>? errors = null) =>
        new(PatternPreviewOutcomeKind.InvalidRequest, Error: error, Errors: errors);

    public static PatternPreviewOutcome ProviderError(string error) =>
        new(PatternPreviewOutcomeKind.ProviderUnavailable, Error: error);

    public static PatternPreviewOutcome NotFound(string error) =>
        new(PatternPreviewOutcomeKind.DataNotFound, Error: error);
}

public sealed record PatternPreviewResult(
    string Symbol,
    TimeFrame TimeFrame,
    bool IsIntraday,
    DateTime DataFrom,
    DateTime DataTo,
    IReadOnlyList<OhlcvBar> Bars,
    IReadOnlyList<PatternPreviewMarker> Markers,
    IReadOnlyList<PatternPreviewMarker> Matches,
    PatternPreviewSummary Summary,
    IReadOnlyList<string> Warnings);

public sealed record PatternPreviewMarker(
    DateTime Date,
    string Type,
    decimal Price,
    decimal? StopPrice = null,
    decimal? TargetPrice = null,
    string? Details = null,
    string? Reason = null);

public sealed record PatternPreviewSummary(
    int MatchCount,
    int EntryCount,
    int ExitCount,
    int ScaleInCount,
    int PartialExitCount,
    int StopMoveCount,
    int SafetyBlockedEntries,
    int CompletedTrades,
    int WinningTrades,
    decimal WinRate,
    decimal CompletedReturnPercent,
    decimal TotalReturnPercent,
    decimal? OpenPositionReturnPercent,
    bool OpenPosition,
    DateTime From,
    DateTime To,
    int RequestedBarCount,
    int DisplayedBarCount);

public interface IPatternPreviewService
{
    Task<PatternPreviewOutcome> PreviewAsync(
        PatternPreviewQuery query,
        CancellationToken ct = default);
}

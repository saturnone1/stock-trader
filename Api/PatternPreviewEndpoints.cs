using StockTrader.Application.StrategyPreview;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Market;

namespace StockTrader.Api;

public sealed record PatternPreviewRequest(
    string Symbol,
    CustomPatternDefinition Pattern,
    TimeFrame TimeFrame = TimeFrame.Daily,
    DateTime? From = null,
    DateTime? To = null);

public static class PatternPreviewEndpoints
{
    public static RouteGroupBuilder MapPatternPreviewApi(this RouteGroupBuilder api)
    {
        api.MapPost("/custom-patterns/preview", PreviewAsync)
            .RequireAuthorization();
        return api;
    }

    private static async Task<IResult> PreviewAsync(
        PatternPreviewRequest request,
        IPatternPreviewService preview,
        IMarketCalendar marketCalendar,
        CancellationToken ct)
    {
        var outcome = await preview.PreviewAsync(
            new PatternPreviewQuery(
                request.Symbol,
                request.Pattern,
                request.TimeFrame,
                request.From,
                request.To),
            ct);

        return outcome.Kind switch
        {
            PatternPreviewOutcomeKind.Success => Results.Ok(
                ToResponse(outcome.Result!, marketCalendar)),
            PatternPreviewOutcomeKind.InvalidRequest => outcome.Errors is { Count: > 0 }
                ? Results.BadRequest(new { error = outcome.Error, errors = outcome.Errors })
                : Results.BadRequest(new { error = outcome.Error }),
            PatternPreviewOutcomeKind.ProviderUnavailable => Results.Json(
                new { error = outcome.Error },
                statusCode: StatusCodes.Status502BadGateway),
            _ => Results.NotFound(new { error = outcome.Error })
        };
    }

    private static object ToResponse(
        PatternPreviewResult result,
        IMarketCalendar marketCalendar) => new
    {
        symbol = result.Symbol,
        timeFrame = result.TimeFrame.ToString(),
        bars = result.Bars.Select(bar => new
        {
            date = bar.Timestamp.ToString("O"),
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            bar.Volume
        }),
        markers = result.Markers.Select(marker => new
        {
            date = marker.Date.ToString("O"),
            type = marker.Type,
            marker.Price,
            marker.StopPrice,
            marker.TargetPrice,
            marker.Details,
            marker.Reason
        }),
        matches = result.Matches.Select(marker => new
        {
            date = marker.Date.ToString("O"),
            marker.Price,
            marker.Details
        }),
        summary = new
        {
            matchCount = result.Summary.MatchCount,
            entryCount = result.Summary.EntryCount,
            exitCount = result.Summary.ExitCount,
            scaleInCount = result.Summary.ScaleInCount,
            partialExitCount = result.Summary.PartialExitCount,
            stopMoveCount = result.Summary.StopMoveCount,
            safetyBlockedEntries = result.Summary.SafetyBlockedEntries,
            completedTrades = result.Summary.CompletedTrades,
            winningTrades = result.Summary.WinningTrades,
            winRate = result.Summary.WinRate,
            completedReturnPercent = result.Summary.CompletedReturnPercent,
            totalReturnPercent = result.Summary.TotalReturnPercent,
            openPositionReturnPercent = result.Summary.OpenPositionReturnPercent,
            openPosition = result.Summary.OpenPosition,
            from = result.Summary.From.ToString("O"),
            to = result.Summary.To.ToString("O"),
            requestedFrom = result.IsIntraday
                ? DisplayMarketTime(result.DataFrom, marketCalendar)
                : result.DataFrom.ToString("yyyy-MM-dd"),
            requestedTo = result.IsIntraday
                ? DisplayMarketTime(result.DataTo, marketCalendar)
                : result.DataTo.AddDays(-1).ToString("yyyy-MM-dd"),
            requestedBarCount = result.Summary.RequestedBarCount,
            displayedBarCount = result.Summary.DisplayedBarCount
        },
        warnings = result.Warnings
    };

    private static string DisplayMarketTime(
        DateTime value,
        IMarketCalendar marketCalendar)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        var marketTime = TimeZoneInfo.ConvertTimeFromUtc(
            utc, marketCalendar.GetTimeZone(MarketType.US));
        return $"{marketTime:yyyy-MM-dd HH:mm} ET";
    }
}

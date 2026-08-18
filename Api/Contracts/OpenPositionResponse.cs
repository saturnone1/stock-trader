using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Api.Contracts;

public sealed record OpenPositionResponse(
    long Id,
    string Symbol,
    string Sector,
    int Quantity,
    decimal EntryPrice,
    decimal CurrentPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    string Pattern,
    decimal UnrealizedPnL,
    int AccountId,
    decimal HighSinceEntry,
    decimal EntryAtr,
    int HoldingDays,
    string OpenedAt,
    string ExitStatus,
    string? ExitRequestedAt,
    string? ExitRequestReason,
    bool HasExitOrderId,
    long ExitPendingSeconds,
    int ExitRequestQuantity,
    bool ExitRequestMarksPartialProfit);

public static class OpenPositionResponseMapper
{
    public static OpenPositionResponse Map(Position position, DateTime utcNow)
    {
        var exit = LiveExitIntentStatusPolicy.Evaluate(position, utcNow);
        return new OpenPositionResponse(
            position.Id,
            position.Symbol,
            position.Sector,
            position.Quantity,
            position.EntryPrice,
            position.CurrentPrice,
            position.StopLossPrice,
            position.TargetPrice,
            position.PatternType.ToString(),
            position.UnrealizedPnL,
            position.AccountId,
            position.HighSinceEntry,
            position.EntryAtr,
            Math.Max(0, (utcNow - position.OpenedAt).Days),
            position.OpenedAt.ToString("o"),
            exit.State.ToString(),
            exit.RequestedAt?.ToString("o"),
            exit.Reason,
            exit.HasBrokerOrderId,
            exit.PendingSeconds,
            exit.RequestedQuantity,
            exit.MarksPartialProfit);
    }
}

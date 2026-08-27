using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Services.TradingCore;

internal static class TradingCoreProjectionMapper
{
    public static Position Position(TradingPositionProjection value) => new()
    {
        Id = ExternalId(value.PositionId),
        SourceSignalId = NullableId(value.SourceSignalId),
        AccountId = IntId(value.AccountId),
        Symbol = value.Symbol,
        Sector = value.Sector,
        Quantity = value.Quantity,
        InitialQuantity = value.InitialQuantity,
        EntryPrice = value.EntryPrice,
        CurrentPrice = value.CurrentPrice,
        StopLossPrice = value.StopLossPrice,
        TargetPrice = value.TargetPrice,
        PatternType = Pattern(value.PatternCode, value.CustomPatternName),
        CustomPatternName = value.CustomPatternName,
        OpenedAt = value.OpenedAtUtc,
        ClosedAt = value.ClosedAtUtc,
        ExitPrice = value.ExitPrice,
        HighSinceEntry = value.HighSinceEntry,
        EntryAtr = value.EntryAtr,
        InitialRiskDistance = value.InitialRiskDistance,
        BreakevenApplied = value.BreakevenApplied,
        TrailingStopActivated = value.TrailingStopActivated,
        PartialProfitTaken = value.PartialProfitTaken,
        ExecutionRequestedAt = value.ExecutionRequestedAtUtc,
        ExecutionRequestReason = value.ExecutionRequestReason,
        ExecutionRequestQuantity = value.ExecutionRequestQuantity,
        ExecutionRequestMarksPartialProfit = value.ExecutionRequestMarksPartialProfit,
        ExecutionRequestKind = PositionKind(value.ExecutionRequestKind),
        ExecutionRequestRuleIndex = value.ExecutionRequestRuleIndex,
        ExecutionOrderId = value.ExecutionOrderId,
        ScalingExecutions = value.ScalingExecutions.Select(item => new PositionScalingExecution
        {
            RuleIndex = item.RuleIndex,
            ExecutionCount = item.ExecutionCount,
        }).ToList(),
    };

    public static TradeRecord Trade(TradingTradeProjection value) => new()
    {
        Id = ExternalId(value.TradeId),
        SourceSignalId = NullableId(value.SourceSignalId),
        Symbol = value.Symbol,
        PatternType = Pattern(value.PatternCode, value.CustomPatternName),
        CustomPatternName = value.CustomPatternName,
        EntryPrice = value.EntryPrice,
        ExitPrice = value.ExitPrice,
        Quantity = value.Quantity,
        EntryTime = value.EntryTimeUtc,
        ExitTime = value.ExitTimeUtc,
        PnL = value.PnL,
        PnLPercent = value.PnLPercent,
        ExitReason = value.ExitReason,
    };

    public static long ExternalId(string value)
    {
        if (long.TryParse(value, out var parsed) && parsed > 0)
            return parsed;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var external = BinaryPrimitives.ReadInt64BigEndian(hash) & long.MaxValue;
        return external == 0 ? 1 : external;
    }

    public static long? NullableId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ExternalId(value);

    public static int IntId(string value) =>
        int.TryParse(value, out var parsed) && parsed >= 0
            ? parsed
            : throw new InvalidOperationException("invalid-trading-core-account-identity");

    public static PatternType Pattern(string code, string? customName)
    {
        if (Enum.TryParse<PatternType>(code, ignoreCase: true, out var pattern)
            && PatternCatalog.TryGet(pattern, out _))
            return pattern;
        if (!string.IsNullOrWhiteSpace(customName))
            return PatternType.Custom;
        throw new InvalidOperationException("invalid-trading-core-pattern-identity");
    }

    private static PositionExecutionKind? PositionKind(string? action) => action switch
    {
        null or "" => null,
        TradingPositionActionKinds.FullExit => PositionExecutionKind.FullExit,
        TradingPositionActionKinds.PartialExit => PositionExecutionKind.PartialProfit,
        TradingPositionActionKinds.ScaleIn => PositionExecutionKind.ScaleIn,
        TradingPositionActionKinds.ScaleOut => PositionExecutionKind.ScaleOut,
        _ => throw new InvalidOperationException("invalid-trading-core-position-action"),
    };
}

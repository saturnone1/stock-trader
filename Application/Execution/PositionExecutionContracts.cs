using StockTrader.Models.Enums;

namespace StockTrader.Application.Execution;

/// <summary>브로커 호출 전에 DB에 원자적으로 선점하는 포지션 실행 의도.</summary>
public sealed record PositionExecutionClaim(
    long PositionId,
    DateTime RequestedAt,
    string Reason,
    int ExpectedPositionQuantity,
    int Quantity,
    PositionExecutionKind Kind = PositionExecutionKind.FullExit,
    int? ScalingRuleIndex = null,
    bool MarksPartialProfit = false);

/// <summary>브로커에서 확인한 체결을 포지션과 원장에 원자 반영하기 위한 값.</summary>
public sealed record PositionExecutionFill(
    long PositionId,
    DateTime RequestedAt,
    int ExpectedPositionQuantity,
    int FilledQuantity,
    decimal FillPrice,
    DateTime FilledAt,
    string? OrderId,
    PositionExecutionKind Kind = PositionExecutionKind.FullExit,
    int? ScalingRuleIndex = null,
    bool MarksPartialProfit = false);

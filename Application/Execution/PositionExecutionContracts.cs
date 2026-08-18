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

/// <summary>매도 체결과 함께 원자적으로 기록할 실현 거래 값.</summary>
public sealed record PositionExecutionTrade(
    string Symbol,
    PatternType PatternType,
    string? CustomPatternName,
    decimal EntryPrice,
    decimal ExitPrice,
    int Quantity,
    DateTime EntryTime,
    DateTime ExitTime,
    decimal PnL,
    decimal PnLPercent,
    string ExitReason);

/// <summary>실시간 포지션 주문의 선점·증거·체결 커밋만 소유하는 저장 포트.</summary>
public interface ILivePositionExecutionStore
{
    Task<bool> TryClaimAsync(PositionExecutionClaim claim, CancellationToken ct = default);
    Task<bool> SetOrderEvidenceAsync(
        long positionId, DateTime requestedAt, string? orderId,
        CancellationToken ct = default);
    Task<bool> ReleaseClaimAsync(
        long positionId, DateTime requestedAt, CancellationToken ct = default);
    Task<bool> CommitFillAsync(
        PositionExecutionFill fill, PositionExecutionTrade? trade,
        CancellationToken ct = default);
}

namespace StockTrader.Application.Execution;

/// <summary>브로커 호출 전에 DB에 원자적으로 선점하는 청산 의도.</summary>
public sealed record PositionExitClaim(
    long PositionId,
    DateTime RequestedAt,
    string Reason,
    int ExpectedPositionQuantity,
    int Quantity,
    bool MarksPartialProfit = false);

/// <summary>브로커에서 확인한 체결을 포지션과 거래 기록에 원자 반영하기 위한 값.</summary>
public sealed record PositionExitFill(
    long PositionId,
    DateTime RequestedAt,
    int ExpectedPositionQuantity,
    int FilledQuantity,
    decimal FillPrice,
    DateTime FilledAt,
    string? OrderId,
    bool MarksPartialProfit);

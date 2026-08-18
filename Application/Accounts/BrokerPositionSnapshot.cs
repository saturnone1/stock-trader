namespace StockTrader.Application.Accounts;

/// <summary>
/// 브로커가 현재 조회 시점에 보고한 보유 종목입니다. 영속 포지션의 전략 상태나
/// 실제 개설 시각을 추측하지 않으며, 계좌 연결 확인에 필요한 값만 전달합니다.
/// </summary>
public sealed record BrokerPositionSnapshot(
    string Symbol,
    int Quantity,
    decimal AverageEntryPrice,
    decimal CurrentPrice);

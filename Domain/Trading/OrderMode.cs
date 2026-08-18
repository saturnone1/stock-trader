namespace StockTrader.Domain.Trading;

/// <summary>
/// 주문 실행 수준의 안정 식별자입니다. 숫자 값과 이름은 저장/API 호환 계약입니다.
/// </summary>
public enum OrderMode
{
    AlertOnly = 0,
    AutoOrder = 1
}

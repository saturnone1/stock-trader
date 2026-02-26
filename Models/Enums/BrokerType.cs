namespace StockTrader.Models.Enums;

/// <summary>
/// 지원하는 브로커 유형.
/// DataSource(데이터 수신)와 별도로 관리 — 주문 실행 브로커는 독립적으로 선택 가능.
/// </summary>
public enum BrokerType
{
    /// <summary>Alpaca Markets (미국 주식, Paper/Live)</summary>
    Alpaca = 0,

    /// <summary>한국투자증권 (KIS Open API) — 구현 예정</summary>
    KoreaInvestment = 10,

    /// <summary>키움증권 (OpenAPI+) — 구현 예정</summary>
    Kiwoom = 11
}

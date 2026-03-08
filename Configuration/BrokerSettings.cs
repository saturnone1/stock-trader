using StockTrader.Models.Enums;

namespace StockTrader.Configuration;

/// <summary>
/// 브로커 설정 — appsettings.json의 "Broker" 섹션에 바인딩.
/// </summary>
public class BrokerSettings
{
    /// <summary>
    /// 기본 사용 브로커 타입.
    /// appsettings.json에서 문자열로 설정 ("Alpaca", "KoreaInvestment", "Kiwoom").
    /// </summary>
    public BrokerType DefaultBrokerType { get; set; } = BrokerType.Alpaca;
}

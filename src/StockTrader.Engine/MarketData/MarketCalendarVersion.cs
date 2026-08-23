namespace StockTrader.Domain.MarketData;

/// <summary>
/// 거래소 휴장·조기마감 근거의 버전 식별자.
/// 캘린더 내용이 바뀌면 이 값도 함께 올라가야 하며, 결과 메타데이터에 기록되어
/// 과거 결과가 어떤 근거로 산출되었는지 사후에 확인할 수 있게 한다.
/// </summary>
public static class MarketCalendarVersion
{
    /// <summary>형식: {거래소 데이터 기준연도 범위}.{개정 번호}</summary>
    public const string Current = "2024-2027.1";
}

namespace StockTrader.Domain.Strategies;

/// <summary>
/// 저장 전략 이름의 비교 키를 만든다. 표시 이름은 보존하되 공백과 대소문자 차이는
/// 같은 이름으로 취급하며, 이 값만 데이터베이스 고유 인덱스에 저장한다.
/// </summary>
public static class StoredStrategyName
{
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Trim().ToUpperInvariant();
    }
}

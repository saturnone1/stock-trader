namespace StockTrader.Domain.MarketData;

/// <summary>시세와 거래 일정에서 사용하는 안정적인 시장 식별자입니다.</summary>
public enum MarketRegion
{
    UnitedStates = 0,
    Korea = 1
}

public sealed record MarketRegionDescriptor(
    MarketRegion Value,
    string DisplayName,
    string TimeZoneId,
    TimeSpan RegularOpen,
    TimeSpan RegularClose);

/// <summary>시장 표시명, 시간대, 정규장 시간을 소유하는 단일 카탈로그입니다.</summary>
public static class MarketRegionCatalog
{
    private static readonly IReadOnlyDictionary<MarketRegion, MarketRegionDescriptor> Descriptors =
        new Dictionary<MarketRegion, MarketRegionDescriptor>
        {
            [MarketRegion.UnitedStates] = new(
                MarketRegion.UnitedStates,
                "미국",
                "America/New_York",
                new TimeSpan(9, 30, 0),
                new TimeSpan(16, 0, 0)),
            [MarketRegion.Korea] = new(
                MarketRegion.Korea,
                "한국",
                "Asia/Seoul",
                new TimeSpan(9, 0, 0),
                new TimeSpan(15, 30, 0))
        };

    public static IReadOnlyCollection<MarketRegionDescriptor> All { get; } =
        Descriptors.Values.ToArray();

    public static MarketRegionDescriptor Get(MarketRegion value) =>
        Descriptors.TryGetValue(value, out var descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(
                nameof(value), value, "지원하지 않는 시장입니다.");
}

using StockTrader.Models.Enums;

namespace StockTrader.Domain.MarketData;

/// <summary>시세 공급자 어댑터가 실제로 보장하는 기능과 조회 제한의 단일 원천.</summary>
public sealed record DataProviderDescriptor(
    DataSource Value,
    string DisplayName,
    bool IsImplemented,
    string Market,
    IReadOnlyList<TimeFrame> SupportedTimeFrames,
    IReadOnlyDictionary<TimeFrame, int> MaximumLookbackDays);

public static class DataProviderCatalog
{
    private static readonly TimeFrame[] AllTimeFrames = Enum.GetValues<TimeFrame>();

    private static readonly IReadOnlyDictionary<DataSource, DataProviderDescriptor> Descriptors =
        new Dictionary<DataSource, DataProviderDescriptor>
        {
            [DataSource.Alpaca] = new(DataSource.Alpaca, "Alpaca", true, "미국", AllTimeFrames, new Dictionary<TimeFrame, int>()),
            [DataSource.Yahoo] = new(DataSource.Yahoo, "Yahoo Finance", true, "미국", AllTimeFrames,
                new Dictionary<TimeFrame, int>
                {
                    [TimeFrame.OneMinute] = 7,
                    [TimeFrame.FiveMinute] = 60,
                    [TimeFrame.FifteenMinute] = 60
                }),
            [DataSource.LsSecurities] = new(DataSource.LsSecurities, "LS증권", true, "한국", AllTimeFrames,
                new Dictionary<TimeFrame, int>
                {
                    [TimeFrame.OneMinute] = 365,
                    [TimeFrame.FiveMinute] = 365,
                    [TimeFrame.FifteenMinute] = 365
                }),
            [DataSource.Polygon] = new(DataSource.Polygon, "Polygon", false, "미국", [], new Dictionary<TimeFrame, int>())
        };

    public static IReadOnlyCollection<DataProviderDescriptor> All { get; } = Descriptors.Values.ToArray();
    public static IReadOnlyCollection<DataProviderDescriptor> Implemented { get; } =
        Descriptors.Values.Where(item => item.IsImplemented).ToArray();

    public static DataProviderDescriptor Get(DataSource value) =>
        Descriptors.TryGetValue(value, out var descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(nameof(value), value, "지원하지 않는 데이터 공급자입니다.");

    public static int? MaximumLookbackDays(DataSource provider, TimeFrame timeFrame) =>
        Get(provider).MaximumLookbackDays.TryGetValue(timeFrame, out var days) ? days : null;
}

using StockTrader.Models.Enums;

namespace StockTrader.Domain.MarketData;

/// <summary>
/// 시간축 자체의 변하지 않는 사실을 보관한다. 조회 범위나 화면 기본값 같은 기능 정책은
/// Application 계층의 전용 정책에서 관리한다.
/// </summary>
public sealed record TimeFrameDescriptor(
    TimeFrame Value,
    string DisplayName,
    bool IsIntraday,
    decimal AnnualizationPeriods);

public static class TimeFrameCatalog
{
    private static readonly IReadOnlyDictionary<TimeFrame, TimeFrameDescriptor> Descriptors =
        new Dictionary<TimeFrame, TimeFrameDescriptor>
        {
            [TimeFrame.OneMinute] = new(TimeFrame.OneMinute, "1분봉", true, 252m * 390m),
            [TimeFrame.FiveMinute] = new(TimeFrame.FiveMinute, "5분봉", true, 252m * 78m),
            [TimeFrame.FifteenMinute] = new(TimeFrame.FifteenMinute, "15분봉", true, 252m * 26m),
            [TimeFrame.Daily] = new(TimeFrame.Daily, "일봉", false, 252m),
            [TimeFrame.Weekly] = new(TimeFrame.Weekly, "주봉", false, 52m)
        };

    public static IReadOnlyCollection<TimeFrameDescriptor> All { get; } = Descriptors.Values.ToArray();

    public static TimeFrameDescriptor Get(TimeFrame timeFrame) =>
        Descriptors.TryGetValue(timeFrame, out var descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "지원하지 않는 시간축입니다.");

    public static bool IsIntraday(TimeFrame timeFrame) => Get(timeFrame).IsIntraday;

    public static string DisplayName(TimeFrame timeFrame) => Get(timeFrame).DisplayName;

    public static decimal AnnualizationPeriods(TimeFrame timeFrame) =>
        Get(timeFrame).AnnualizationPeriods;
}

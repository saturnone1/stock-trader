using StockTrader.Models.Enums;

namespace StockTrader.Application.StrategyPreview;

public sealed record PreviewTimeFrameSettings(
    Func<DateTime, DateTime> DefaultFrom,
    TimeSpan MaximumRange,
    TimeSpan WarmupRange,
    TimeSpan CoverageTolerance);

/// <summary>전략 미리보기의 조회 범위와 데이터 충족 기준을 한곳에서 관리한다.</summary>
public static class PreviewTimeFramePolicy
{
    private static readonly IReadOnlyDictionary<TimeFrame, PreviewTimeFrameSettings> Settings =
        new Dictionary<TimeFrame, PreviewTimeFrameSettings>
        {
            [TimeFrame.OneMinute] = new(
                to => to.AddDays(-1), TimeSpan.FromDays(7), TimeSpan.FromDays(3), TimeSpan.FromDays(4)),
            [TimeFrame.FiveMinute] = new(
                to => to.AddDays(-5), TimeSpan.FromDays(31), TimeSpan.FromDays(14), TimeSpan.FromDays(4)),
            [TimeFrame.FifteenMinute] = new(
                to => to.AddDays(-20), TimeSpan.FromDays(120), TimeSpan.FromDays(45), TimeSpan.FromDays(4)),
            [TimeFrame.Daily] = new(
                to => to.AddYears(-1), TimeSpan.FromDays(365 * 5), TimeSpan.FromDays(400), TimeSpan.FromDays(5)),
            [TimeFrame.Weekly] = new(
                to => to.AddYears(-5), TimeSpan.FromDays(365 * 15), TimeSpan.FromDays(365 * 5), TimeSpan.FromDays(14))
        };

    public static PreviewTimeFrameSettings Get(TimeFrame timeFrame) =>
        Settings.TryGetValue(timeFrame, out var settings)
            ? settings
            : throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "지원하지 않는 미리보기 시간축입니다.");
}

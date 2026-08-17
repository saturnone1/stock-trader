using StockTrader.Models.Enums;

namespace StockTrader.Application.Backtesting;

public sealed record BacktestTimeFrameSettings(
    int WarmupCalendarDays,
    int SimulationWindowBars);

/// <summary>
/// 백테스트 데이터 준비와 시뮬레이션 창 크기의 단일 출처다.
/// 데이터 공급자 자체의 조회 제한은 Infrastructure capability에서 별도로 관리한다.
/// </summary>
public static class BacktestTimeFramePolicy
{
    private static readonly IReadOnlyDictionary<TimeFrame, BacktestTimeFrameSettings> Settings =
        new Dictionary<TimeFrame, BacktestTimeFrameSettings>
        {
            [TimeFrame.OneMinute] = new(WarmupCalendarDays: 2, SimulationWindowBars: 800),
            [TimeFrame.FiveMinute] = new(WarmupCalendarDays: 10, SimulationWindowBars: 800),
            [TimeFrame.FifteenMinute] = new(WarmupCalendarDays: 15, SimulationWindowBars: 600),
            [TimeFrame.Daily] = new(WarmupCalendarDays: 400, SimulationWindowBars: 260),
            [TimeFrame.Weekly] = new(WarmupCalendarDays: 365 * 5, SimulationWindowBars: 260)
        };

    public static BacktestTimeFrameSettings Get(TimeFrame timeFrame) =>
        Settings.TryGetValue(timeFrame, out var settings)
            ? settings
            : throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "지원하지 않는 백테스트 시간축입니다.");
}

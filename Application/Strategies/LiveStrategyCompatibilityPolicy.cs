using StockTrader.Domain.Strategies;
using StockTrader.Models.Enums;

namespace StockTrader.Application.Strategies;

/// <summary>백테스트와 동일한 체결 의미를 보장할 수 있는 실시간 전략 범위.</summary>
public static class LiveStrategyCompatibilityPolicy
{
    public static IReadOnlyList<TimeFrame> SupportedTimeFrames { get; } = [TimeFrame.Daily];
    public static IReadOnlyList<string> SupportedEntryModes { get; } =
        [StrategyCatalog.NextOpenEntryMode];
    public const bool SupportsPartialExit = false;
    public const bool SupportsScaling = false;

    public static IReadOnlyList<string> Validate(CompiledStrategy strategy)
    {
        var errors = new List<string>();
        if (!SupportedTimeFrames.Contains(strategy.TimeFrame))
            errors.Add("실시간 주문은 현재 일봉 전략만 안전하게 지원합니다.");
        if (!SupportedEntryModes.Contains(strategy.EntryMode, StringComparer.OrdinalIgnoreCase))
            errors.Add("실시간 주문은 완료된 일봉 신호를 사용하도록 '다음 봉 시가'만 지원합니다.");
        if ((!SupportsPartialExit && strategy.Source.PartialProfitR > 0)
            || (!SupportsScaling && strategy.ScalingRules.Count > 0))
            errors.Add("부분 익절·추가 매수·분할 매도가 있는 전략은 실시간 주문을 아직 켤 수 없습니다.");
        return errors;
    }
}

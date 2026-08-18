using StockTrader.Application.Backtesting;
using StockTrader.Application.Execution;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 사용자 전략의 종가 청산·분할매매 조건을 백테스트 실행 세션용 지시로 변환합니다.
/// 일반 보유봉과 다음 시가 진입봉은 반드시 이 해석기를 공유합니다.
/// </summary>
internal static class BacktestStrategyExecutionInstructionResolver
{
    public static CompiledStrategyPositionInstructions Resolve(
        BacktestExecutionAdapter.OpenPosition position,
        PreparedSymbolData data,
        int barIndex,
        int maxWindow,
        int maxTotalPositions,
        decimal currentEquity,
        BacktestStrategyRuntimeRegistry runtimeRegistry)
    {
        var detector = runtimeRegistry.FindDetector(position.CustomPatternName);
        if (detector is null)
            return new CompiledStrategyPositionInstructions(null, null);

        var windowSize = Math.Min(barIndex + 1, Math.Max(1, maxWindow));
        var windowStart = barIndex + 1 - windowSize;
        var windowBars = data.Bars[windowStart..(barIndex + 1)];
        var close = data.Bars[barIndex].Close;
        var runtime = runtimeRegistry.Find(position.CustomPatternName);
        var maxPositionCost = PositionScaleInCapacityPolicy.CalculateMaxPositionCost(
            currentEquity,
            maxTotalPositions,
            runtime?.Portfolio.MaxSinglePositionPercent ?? 0m);
        return CompiledStrategyPositionInstructionResolver.Resolve(
            detector,
            windowBars,
            close,
            position.EntryPrice,
            position.ScaleCounts,
            maxPositionCost);
    }
}

using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Services.Backtest;

internal sealed record BacktestPendingEntry(
    PatternType PatternType,
    string? StrategyName,
    decimal SignalEntryPrice,
    decimal SignalStopPrice,
    decimal SignalTargetPrice,
    decimal EntryAtr,
    long EntryVolume,
    decimal EquityAtSignal,
    decimal RiskFraction,
    decimal PositionCapFraction,
    LongPositionExitPolicy? ExitProfile,
    /// <summary>
    /// 신호가 유효한 목표가를 싣지 못했을 때 적용할 R 배수. 신호 시점의 전략 기하에서
    /// 도출해 보관하므로, 체결이 다음 봉으로 미뤄져도 preview 와 같은 목표가가 나온다.
    /// </summary>
    decimal FallbackTargetMultiple);

/// <summary>현재가와 차기봉 시가 진입이 공유하는 백테스트 포지션 생성 계약입니다.</summary>
internal static class BacktestOpenPositionFactory
{
    public static BacktestExecutionAdapter.OpenPosition CreateCurrentClose(
        PatternSignal signal,
        PatternType patternType,
        string? strategyName,
        OhlcvBar entryBar,
        int entryBarIndex,
        int quantity,
        decimal entryAtr,
        decimal equityAtEntry,
        LongPositionExitPolicy? exitProfile) =>
        Create(
            patternType,
            strategyName,
            signal.EntryPrice,
            signal.StopLossPrice,
            signal.TargetPrice,
            Math.Abs(signal.EntryPrice - signal.StopLossPrice),
            entryBar,
            entryBarIndex,
            quantity,
            entryAtr,
            equityAtEntry,
            exitProfile);

    public static BacktestExecutionAdapter.OpenPosition CreateNextOpen(
        BacktestPendingEntry pending,
        LongEntryFill fill,
        OhlcvBar entryBar,
        int entryBarIndex,
        int quantity) =>
        Create(
            pending.PatternType,
            pending.StrategyName,
            fill.EntryPrice,
            fill.StopPrice,
            fill.TargetPrice,
            fill.RiskDistance,
            entryBar,
            entryBarIndex,
            quantity,
            pending.EntryAtr,
            pending.EquityAtSignal,
            pending.ExitProfile);

    private static BacktestExecutionAdapter.OpenPosition Create(
        PatternType patternType,
        string? strategyName,
        decimal entryPrice,
        decimal stopPrice,
        decimal targetPrice,
        decimal riskDistance,
        OhlcvBar entryBar,
        int entryBarIndex,
        int quantity,
        decimal entryAtr,
        decimal equityAtEntry,
        LongPositionExitPolicy? exitProfile) => new()
    {
        PatternType = patternType,
        CustomPatternName = strategyName,
        EntryPrice = entryPrice,
        OriginalStop = stopPrice,
        StopLoss = stopPrice,
        Target = targetPrice,
        Quantity = quantity,
        InitialQuantity = quantity,
        CurrentQuantity = quantity,
        TotalCost = entryPrice * quantity,
        EntryTime = entryBar.Timestamp,
        EntryBarIndex = entryBarIndex,
        EntryAtr = entryAtr,
        EntryVolume = entryBar.Volume,
        HighestHighSinceEntry = entryPrice,
        LowestLowSinceEntry = entryPrice,
        RiskDistance = riskDistance,
        EquityAtEntry = equityAtEntry,
        CustomExitProfile = exitProfile
    };
}

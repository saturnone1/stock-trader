using StockTrader.Models;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

/// <summary>커스텀 전략의 재진입, 연속손실, 낙폭 및 일일 진입 상태입니다.</summary>
internal sealed class BacktestStrategyRuntime
{
    public required ICustomStrategyDetector Detector { get; init; }
    public required CircuitBreakerConfig CircuitBreaker { get; init; }
    public required ReentryConfig Reentry { get; init; }
    public required PortfolioRulesConfig Portfolio { get; init; }
    public int ConsecutiveLosses;
    public int CircuitBreakerUntilStep;
    public decimal RealizedEquity;
    public decimal PeakEquity;
    public bool CircuitBreakerTripped;
    public int DailyEntryCount;
    public DateOnly LastEntryDate = DateOnly.MinValue;
}

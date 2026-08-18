using StockTrader.Application.Strategies;
using StockTrader.Domain.MarketData;

namespace StockTrader.Application.MarketData;

public sealed record DailyMarketDataSyncWindow(
    DateOnly MarketDate,
    bool IsTradingDay,
    bool IsReady);

/// <summary>일봉 동기화 대상과 전략 평가에 필요한 최소 이력을 결정합니다.</summary>
public static class DailyMarketDataSyncPolicy
{
    public const int BootstrapLookbackYears = 5;

    public static IReadOnlyList<string> ResolveRequiredSymbols(
        IEnumerable<string> watchlistSymbols,
        DataSource source) =>
        MarketSymbolPolicy.NormalizeMany(
            watchlistSymbols.Append(DataProviderCatalog.RegimeBenchmarkSymbol(source)));

    public static int MinimumRequiredBars(string symbol, DataSource source) =>
        symbol.Equals(
            DataProviderCatalog.RegimeBenchmarkSymbol(source),
            StringComparison.OrdinalIgnoreCase)
            ? StrategyEvaluationPolicy.RegimeTrendBars
            : StrategyEvaluationPolicy.LiveScannerMinimumBars;

    public static DailyMarketDataSyncWindow EvaluateWindow(
        DateTime marketLocalTime,
        TimeSpan regularClose,
        TimeSpan closeDelay)
    {
        var tradingDay = marketLocalTime.DayOfWeek is not (
            DayOfWeek.Saturday or DayOfWeek.Sunday);
        return new DailyMarketDataSyncWindow(
            DateOnly.FromDateTime(marketLocalTime),
            tradingDay,
            tradingDay && marketLocalTime.TimeOfDay >= regularClose.Add(closeDelay));
    }

    public static bool IsCompletedDailyTimestamp(
        DateTime timestamp,
        DailyMarketDataSyncWindow window)
    {
        var barDate = DateOnly.FromDateTime(timestamp);
        return barDate < window.MarketDate
            || (barDate == window.MarketDate && window.IsReady);
    }
}

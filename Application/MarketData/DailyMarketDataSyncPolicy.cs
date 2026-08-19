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

    /// <summary>
    /// 동기화 대상 날짜와 그날의 완료 여부를 결정한다.
    /// 거래일 여부와 마감 시각은 호출자가 거래소 캘린더에서 조회한 근거로 전달한다.
    /// 조기마감일에는 정규 마감이 아니라 그날의 실제 마감 시각을 기준으로 완료를 판정하므로,
    /// 일찍 끝난 장의 일봉을 정규 마감 시각까지 미완성으로 오해하지 않는다.
    /// </summary>
    public static DailyMarketDataSyncWindow EvaluateWindow(
        DateTime marketLocalTime,
        TradingDayEvidence tradingDayEvidence,
        TimeSpan regularClose,
        TimeSpan closeDelay)
    {
        var tradingDay = tradingDayEvidence.IsTradingDay;
        var effectiveClose = tradingDayEvidence.EarlyCloseTime ?? regularClose;
        return new DailyMarketDataSyncWindow(
            DateOnly.FromDateTime(marketLocalTime),
            tradingDay,
            tradingDay && marketLocalTime.TimeOfDay >= effectiveClose.Add(closeDelay));
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

using Microsoft.Extensions.Caching.Memory;

namespace StockTrader.Data.Repositories;

internal static class TradeReadCache
{
    public const string OpenPositions = "TradeRepo:OpenPositions";
    public const string ActiveSignals = "TradeRepo:ActiveSignals";

    public static string RecentRecommendations(int count) => $"TradeRepo:RecentRecs:{count}";

    public static void InvalidateRecommendations(IMemoryCache cache)
    {
        foreach (var count in new[] { 20, 50, 100, 200 })
            cache.Remove(RecentRecommendations(count));
    }

    public static void InvalidateAcceptedEntry(IMemoryCache cache)
    {
        cache.Remove(OpenPositions);
        InvalidateRecommendations(cache);
    }
}

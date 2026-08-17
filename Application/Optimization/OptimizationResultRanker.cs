namespace StockTrader.Application.Optimization;

public static class OptimizationResultRanker
{
    /// <summary>
    /// 결과 목록을 rankBy 기준으로 정렬하고 상위 maxResults개에 순위를 매깁니다.
    /// </summary>
    public static List<OptimizeResultItem> RankOptimizeResults(
    List<OptimizeResultItem> items, string rankBy, int maxResults)
    {
        IEnumerable<OptimizeResultItem> sorted = rankBy.ToLowerInvariant() switch
        {
            "totalreturn" => items.OrderByDescending(r => r.TotalReturn),
            "sharperation" or
            "sharperatio" => items.OrderByDescending(r => r.SharpeRatio),
            "calmarratio" => items.OrderByDescending(r => r.CalmarRatio),
            "profitfactor" => items.OrderByDescending(r => r.ProfitFactor),
            "winrate" => items.OrderByDescending(r => r.WinRate),
            "annualizedreturn" => items.OrderByDescending(r => r.AnnualizedReturn),
            _ => items.OrderByDescending(r => r.SortinoRatio) // 기본: sortinoRatio
        };

        var ranked = sorted.Take(maxResults).ToList();
        for (int i = 0; i < ranked.Count; i++)
            ranked[i].Rank = i + 1;

        return ranked;
    }
}

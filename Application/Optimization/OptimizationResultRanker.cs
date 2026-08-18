namespace StockTrader.Application.Optimization;

public static class OptimizationResultRanker
{
    /// <summary>
    /// 결과 목록을 rankBy 기준으로 정렬하고 상위 maxResults개에 순위를 매깁니다.
    /// </summary>
    public static List<OptimizeResultItem> RankOptimizeResults(
    List<OptimizeResultItem> items, string rankBy, int maxResults)
    {
        var sorted = OptimizationRankingPolicy.OrderDescending(items, rankBy, r => new(
            r.TotalReturn,
            r.SortinoRatio,
            r.SharpeRatio,
            r.CalmarRatio,
            r.ProfitFactor,
            r.WinRate,
            r.AnnualizedReturn));

        var ranked = sorted.Take(maxResults).ToList();
        for (int i = 0; i < ranked.Count; i++)
            ranked[i].Rank = i + 1;

        return ranked;
    }
}

using StockTrader.Application.MachineLearning;

namespace StockTrader.Services.ML;

internal sealed record MarketRegimeClusterProfile(
    uint ClusterId,
    double AverageReturn20Day,
    double AverageVolatility);

/// <summary>임의 K-Means 번호를 서로 다른 네 가지 투자자 시장 국면에 안정적으로 대응시킵니다.</summary>
internal static class MarketRegimeClusterLabelPolicy
{
    public static IReadOnlyDictionary<uint, string> Assign(
        IReadOnlyList<MarketRegimeClusterProfile> profiles)
    {
        if (profiles.Count != MarketRegimeClusterCatalog.RequiredClusterCount
            || profiles.Select(profile => profile.ClusterId).Distinct().Count()
                != MarketRegimeClusterCatalog.RequiredClusterCount)
        {
            throw new InvalidOperationException(
                "Every configured market regime cluster must contain training evidence.");
        }

        var unassigned = profiles.ToList();
        var highVolatility = unassigned
            .OrderByDescending(profile => profile.AverageVolatility)
            .ThenBy(profile => profile.ClusterId)
            .First();
        unassigned.Remove(highVolatility);

        var bullish = unassigned
            .OrderByDescending(profile => profile.AverageReturn20Day)
            .ThenBy(profile => profile.ClusterId)
            .First();
        unassigned.Remove(bullish);

        var bearish = unassigned
            .OrderBy(profile => profile.AverageReturn20Day)
            .ThenBy(profile => profile.ClusterId)
            .First();
        unassigned.Remove(bearish);

        return new Dictionary<uint, string>
        {
            [highVolatility.ClusterId] = MarketRegimeClusterCatalog.HighVolatility,
            [bullish.ClusterId] = MarketRegimeClusterCatalog.Bullish,
            [bearish.ClusterId] = MarketRegimeClusterCatalog.Bearish,
            [unassigned.Single().ClusterId] = MarketRegimeClusterCatalog.Sideways,
        };
    }
}

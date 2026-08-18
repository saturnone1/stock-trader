using Microsoft.ML;
using StockTrader.Application.MachineLearning;

namespace StockTrader.Services.ML;

internal sealed record FittedMarketRegimeModel(
    ITransformer Model,
    IReadOnlyDictionary<uint, string> ClusterLabels);

/// <summary>K-Means 학습과 학습 표본 기반 클러스터 의미 부여를 소유합니다.</summary>
internal sealed class MarketRegimeModelTrainer(MLContext mlContext)
{
    public FittedMarketRegimeModel Fit(
        IReadOnlyList<MarketRegimeFeatures> features,
        int clusterCount)
    {
        if (clusterCount != MarketRegimeClusterCatalog.RequiredClusterCount)
        {
            throw new InvalidOperationException(
                $"Market regime classification requires exactly "
                + $"{MarketRegimeClusterCatalog.RequiredClusterCount} clusters.");
        }

        var inputs = features.Select(MarketRegimeFeatureCatalog.ToModelInput).ToArray();
        var data = mlContext.Data.LoadFromEnumerable(inputs);
        var pipeline = mlContext.Transforms
            .Concatenate("Features", MarketRegimeFeatureCatalog.ColumnNames)
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.Clustering.Trainers.KMeans(
                featureColumnName: "Features",
                numberOfClusters: clusterCount));
        var model = pipeline.Fit(data);
        var clusterIds = mlContext.Data
            .CreateEnumerable<RegimeClusterOutput>(
                model.Transform(data), reuseRowObject: false)
            .Select(output => output.ClusterId)
            .ToArray();

        var profiles = features.Zip(clusterIds)
            .GroupBy(pair => pair.Second)
            .Select(group => new MarketRegimeClusterProfile(
                group.Key,
                group.Average(pair => pair.First.Return20Day),
                group.Average(pair => pair.First.VolatilityLevel)))
            .ToArray();
        return new FittedMarketRegimeModel(
            model,
            MarketRegimeClusterLabelPolicy.Assign(profiles));
    }
}

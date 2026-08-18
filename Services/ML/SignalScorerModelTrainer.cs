using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;
using StockTrader.Models;

namespace StockTrader.Services.ML;

internal sealed record FittedSignalScorer(
    ITransformer Model,
    double Accuracy,
    double Auc,
    List<FeatureImportance> FeatureImportances);

/// <summary>시간순 데이터로 FastTree를 학습하고 미래 검증 피처 중요도를 측정합니다.</summary>
internal sealed class SignalScorerModelTrainer(MLContext mlContext)
{
    public FittedSignalScorer Fit(SignalScoringDatasetSplit split)
    {
        var training = split.Training
            .Select(sample => SignalScoringFeatureCatalog.ToModelInput(
                sample.Features, sample.IsWin))
            .ToList();
        var validation = split.Validation
            .Select(sample => SignalScoringFeatureCatalog.ToModelInput(
                sample.Features, sample.IsWin))
            .ToList();
        var trainingView = mlContext.Data.LoadFromEnumerable(training);
        var pipeline = mlContext.Transforms
            .Concatenate("Features", SignalScoringFeatureCatalog.ColumnNames)
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.BinaryClassification.Trainers.FastTree(
                new FastTreeBinaryTrainer.Options
                {
                    LabelColumnName = "Label",
                    FeatureColumnName = "Features",
                    NumberOfLeaves = 20,
                    NumberOfTrees = 100,
                    MinimumExampleCountPerLeaf = 5,
                    LearningRate = 0.1,
                }));
        var model = pipeline.Fit(trainingView);
        var metrics = Evaluate(model, validation);
        return new FittedSignalScorer(
            model,
            metrics.Accuracy,
            metrics.AreaUnderRocCurve,
            ComputePermutationImportances(model, validation, metrics));
    }

    private CalibratedBinaryClassificationMetrics Evaluate(
        ITransformer model,
        IReadOnlyList<SignalScorerInput> samples)
    {
        var view = mlContext.Data.LoadFromEnumerable(samples);
        return mlContext.BinaryClassification.Evaluate(
            model.Transform(view),
            labelColumnName: "Label");
    }

    private List<FeatureImportance> ComputePermutationImportances(
        ITransformer model,
        IReadOnlyList<SignalScorerInput> validation,
        CalibratedBinaryClassificationMetrics baseline)
    {
        var raw = new double[SignalScoringFeatureCatalog.All.Count];
        for (var featureIndex = 0; featureIndex < raw.Length; featureIndex++)
        {
            var descriptor = SignalScoringFeatureCatalog.All[featureIndex];
            var permuted = validation
                .Select(SignalScoringFeatureCatalog.Clone)
                .ToArray();
            var values = permuted.Select(descriptor.Read).ToArray();
            Shuffle(values, new Random(42 + featureIndex));
            for (var index = 0; index < permuted.Length; index++)
                descriptor.Write(permuted[index], values[index]);
            var metrics = Evaluate(model, permuted);
            raw[featureIndex] = Math.Max(
                0,
                baseline.AreaUnderRocCurve - metrics.AreaUnderRocCurve);
        }

        var total = raw.Sum();
        return SignalScoringFeatureCatalog.All
            .Select((descriptor, index) => new FeatureImportance
            {
                FeatureName = descriptor.DisplayName,
                Importance = total > 0 ? raw[index] / total : 0,
            })
            .OrderByDescending(feature => feature.Importance)
            .ThenBy(feature => feature.FeatureName, StringComparer.Ordinal)
            .ToList();
    }

    private static void Shuffle(float[] values, Random random)
    {
        for (var index = values.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (values[index], values[swap]) = (values[swap], values[index]);
        }
    }
}

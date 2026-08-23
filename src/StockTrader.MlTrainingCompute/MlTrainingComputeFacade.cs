using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;
using StockTrader.ServiceContracts.MachineLearning;

namespace StockTrader.MlTrainingCompute;

public sealed record MlComputeResult(
    string Status,
    string Message,
    MlModelArtifactContract? RegimeArtifact,
    MlModelArtifactContract? SignalArtifact);

public static class MlTrainingComputeFacade
{
    private const double ValidationFraction = 0.2;
    private static readonly string[] RegimeColumns =
    [
        nameof(RegimeInput.Return5Day), nameof(RegimeInput.Return10Day),
        nameof(RegimeInput.Return20Day), nameof(RegimeInput.VolatilityLevel),
        nameof(RegimeInput.VolumeChangeRate), nameof(RegimeInput.MaSlopePercent),
        nameof(RegimeInput.Rsi),
    ];
    private static readonly SignalFeature[] SignalFeatures =
    [
        new(nameof(SignalInput.PatternTypeCode), "패턴 유형", x => x.PatternTypeCode, (x,v) => x.PatternTypeCode=v),
        new(nameof(SignalInput.Rsi), "RSI", x => x.Rsi, (x,v) => x.Rsi=v),
        new(nameof(SignalInput.BollingerPosition), "볼린저 위치", x => x.BollingerPosition, (x,v) => x.BollingerPosition=v),
        new(nameof(SignalInput.VolumeRatio), "거래량 비율", x => x.VolumeRatio, (x,v) => x.VolumeRatio=v),
        new(nameof(SignalInput.MarketRegimeCode), "시장 레짐", x => x.MarketRegimeCode, (x,v) => x.MarketRegimeCode=v),
        new(nameof(SignalInput.AtrPercent), "ATR%", x => x.AtrPercent, (x,v) => x.AtrPercent=v),
        new(nameof(SignalInput.HistoricalWinRate), "역사적 승률", x => x.HistoricalWinRate, (x,v) => x.HistoricalWinRate=v),
        new(nameof(SignalInput.RiskRewardRatio), "계획 손익비", x => x.RiskRewardRatio, (x,v) => x.RiskRewardRatio=v),
        new(nameof(SignalInput.PriceVsLongMovingAverage), "장기 이동평균 대비 위치", x => x.PriceVsLongMovingAverage, (x,v) => x.PriceVsLongMovingAverage=v),
        new(nameof(SignalInput.LongTrendHistoryAvailable), "장기 추세 이력 보유", x => x.LongTrendHistoryAvailable, (x,v) => x.LongTrendHistoryAvailable=v),
    ];

    public static MlComputeResult Train(MlTrainingJobRequest request, CancellationToken ct = default)
    {
        var error = MlTrainingContractPolicy.CompatibilityError(request);
        if (error is not null) throw new ArgumentException(error, nameof(request));
        ct.ThrowIfCancellationRequested();
        var ml = new MLContext(seed: 42);
        var regime = request.RegimeSamples.Count >= request.MinimumTrainingSamples
            ? TrainRegime(ml, request, ct) : null;
        ct.ThrowIfCancellationRequested();
        var signal = request.SignalSamples.Count >= request.MinimumTrainingSamples
            ? TrainSignal(ml, request, ct) : null;
        var status = regime is not null && signal is not null
            ? MlTrainingJobStatuses.Completed
            : regime is not null || signal is not null
                ? MlTrainingJobStatuses.PartiallyCompleted
                : MlTrainingJobStatuses.InsufficientData;
        return new(status,
            $"regime={regime?.TrainingSamples ?? 0};signal={signal?.TrainingSamples ?? 0}",
            regime, signal);
    }

    public static uint PredictRegime(byte[] modelBytes, MlRegimeFeatureContract features)
    {
        var ml = new MLContext(seed: 42);
        using var stream = new MemoryStream(modelBytes, writable: false);
        var model = ml.Model.Load(stream, out _);
        return ml.Model.CreatePredictionEngine<RegimeInput, RegimeOutput>(model)
            .Predict(RegimeInput.From(features)).ClusterId;
    }

    public static float PredictSignal(byte[] modelBytes, MlSignalFeatureContract features)
    {
        var ml = new MLContext(seed: 42);
        using var stream = new MemoryStream(modelBytes, writable: false);
        var model = ml.Model.Load(stream, out _);
        return ml.Model.CreatePredictionEngine<SignalInput, SignalOutput>(model)
            .Predict(SignalInput.From(features, false)).Probability;
    }

    private static MlModelArtifactContract TrainRegime(
        MLContext ml, MlTrainingJobRequest request, CancellationToken ct)
    {
        var samples = request.RegimeSamples.Select(RegimeInput.From).ToArray();
        var data = ml.Data.LoadFromEnumerable(samples);
        var pipeline = ml.Transforms.Concatenate("Features", RegimeColumns)
            .Append(ml.Transforms.NormalizeMinMax("Features"))
            .Append(ml.Clustering.Trainers.KMeans(
                featureColumnName: "Features",
                numberOfClusters: request.RegimeClusterCount));
        ct.ThrowIfCancellationRequested();
        var model = pipeline.Fit(data);
        var ids = ml.Data.CreateEnumerable<RegimeOutput>(model.Transform(data), false)
            .Select(x => x.ClusterId).ToArray();
        var profiles = request.RegimeSamples.Zip(ids)
            .GroupBy(x => x.Second)
            .Select(g => new RegimeProfile(g.Key, g.Average(x => x.First.Return20Day),
                g.Average(x => x.First.VolatilityLevel))).ToArray();
        var labels = AssignLabels(profiles);
        return Artifact(ml, model, MlModelKinds.MarketRegime,
            MlTrainingContractVersions.RegimeFeatureSchema,
            MlTrainingContractVersions.RegimeFeatureCount, request,
            samples.Length, null, null, labels, []);
    }

    private static MlModelArtifactContract? TrainSignal(
        MLContext ml, MlTrainingJobRequest request, CancellationToken ct)
    {
        var ordered = request.SignalSamples.ToArray();
        var validationCount = (int)Math.Ceiling(ordered.Length * ValidationFraction);
        var trainingCount = ordered.Length - validationCount;
        if (trainingCount < 2 || validationCount < 2) return null;
        var training = ordered[..trainingCount].Select(x => SignalInput.From(x.Features, x.IsWin)).ToArray();
        var validation = ordered[trainingCount..].Select(x => SignalInput.From(x.Features, x.IsWin)).ToArray();
        if (!BothLabels(training) || !BothLabels(validation)) return null;
        var data = ml.Data.LoadFromEnumerable(training);
        var pipeline = ml.Transforms.Concatenate("Features", SignalFeatures.Select(x => x.Column).ToArray())
            .Append(ml.Transforms.NormalizeMinMax("Features"))
            .Append(ml.BinaryClassification.Trainers.FastTree(new FastTreeBinaryTrainer.Options
            {
                LabelColumnName = "Label", FeatureColumnName = "Features",
                NumberOfLeaves = 20, NumberOfTrees = 100,
                MinimumExampleCountPerLeaf = 5, LearningRate = 0.1,
            }));
        ct.ThrowIfCancellationRequested();
        var model = pipeline.Fit(data);
        var baseline = Evaluate(ml, model, validation);
        var importance = Importances(ml, model, validation, baseline.AreaUnderRocCurve);
        return Artifact(ml, model, MlModelKinds.SignalScorer,
            MlTrainingContractVersions.SignalFeatureSchema,
            MlTrainingContractVersions.SignalFeatureCount, request, ordered.Length,
            baseline.Accuracy, baseline.AreaUnderRocCurve, null, importance);
    }

    private static MlModelArtifactContract Artifact(
        MLContext ml, ITransformer model, string kind, int schema, int count,
        MlTrainingJobRequest request, int samples, double? accuracy, double? auc,
        IReadOnlyDictionary<uint,string>? labels,
        IReadOnlyList<MlFeatureImportanceContract> importances)
    {
        using var stream = new MemoryStream();
        ml.Model.Save(model, null, stream);
        var bytes = stream.ToArray();
        var artifact = new MlModelArtifactContract(
            MlTrainingContractVersions.Current, string.Empty, kind,
            request.TrainerVersion, schema, count, request.RequestedAtUtc,
            request.ObservationCutoffUtc, samples, accuracy, auc, labels,
            importances, MlTrainingContractHash.Sha256(bytes), bytes);
        return artifact with { ArtifactId = MlTrainingContractHash.Artifact(artifact) };
    }

    private static CalibratedBinaryClassificationMetrics Evaluate(
        MLContext ml, ITransformer model, IReadOnlyList<SignalInput> values) =>
        ml.BinaryClassification.Evaluate(model.Transform(ml.Data.LoadFromEnumerable(values)), "Label");

    private static IReadOnlyList<MlFeatureImportanceContract> Importances(
        MLContext ml, ITransformer model, IReadOnlyList<SignalInput> validation, double baseline)
    {
        var raw = new double[SignalFeatures.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            var descriptor = SignalFeatures[i];
            var permuted = validation.Select(x => x.Clone()).ToArray();
            var values = permuted.Select(descriptor.Read).ToArray();
            Shuffle(values, new Random(42 + i));
            for (var j = 0; j < values.Length; j++) descriptor.Write(permuted[j], values[j]);
            raw[i] = Math.Max(0, baseline - Evaluate(ml, model, permuted).AreaUnderRocCurve);
        }
        var total = raw.Sum();
        return SignalFeatures.Select((x, i) => new MlFeatureImportanceContract(
                x.Display, total > 0 ? raw[i] / total : 0))
            .OrderByDescending(x => x.Importance).ThenBy(x => x.FeatureName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyDictionary<uint,string> AssignLabels(IReadOnlyList<RegimeProfile> profiles)
    {
        if (profiles.Count != MlTrainingContractVersions.RequiredRegimeClusters)
            throw new InvalidOperationException("Every regime cluster must contain training evidence.");
        var remaining = profiles.ToList();
        var volatileProfile = remaining.OrderByDescending(x => x.Volatility).ThenBy(x => x.Id).First();
        remaining.Remove(volatileProfile);
        var bullish = remaining.OrderByDescending(x => x.Return20).ThenBy(x => x.Id).First();
        remaining.Remove(bullish);
        var bearish = remaining.OrderBy(x => x.Return20).ThenBy(x => x.Id).First();
        remaining.Remove(bearish);
        return new Dictionary<uint,string>
        {
            [volatileProfile.Id] = MlRegimeLabels.HighVolatility,
            [bullish.Id] = MlRegimeLabels.Bullish,
            [bearish.Id] = MlRegimeLabels.Bearish,
            [remaining.Single().Id] = MlRegimeLabels.Sideways,
        };
    }

    private static bool BothLabels(IEnumerable<SignalInput> values) =>
        values.Any(x => x.Label) && values.Any(x => !x.Label);
    private static void Shuffle(float[] values, Random random)
    {
        for (var i = values.Length - 1; i > 0; i--)
        { var j = random.Next(i + 1); (values[i], values[j]) = (values[j], values[i]); }
    }

    private sealed record RegimeProfile(uint Id, double Return20, double Volatility);
    private sealed record SignalFeature(string Column, string Display,
        Func<SignalInput,float> Read, Action<SignalInput,float> Write);
}

public sealed class RegimeInput
{
    public float Return5Day { get; set; }
    public float Return10Day { get; set; }
    public float Return20Day { get; set; }
    public float VolatilityLevel { get; set; }
    public float VolumeChangeRate { get; set; }
    public float MaSlopePercent { get; set; }
    public float Rsi { get; set; }
    public static RegimeInput From(MlRegimeFeatureContract x) => new()
    { Return5Day=x.Return5Day, Return10Day=x.Return10Day, Return20Day=x.Return20Day,
      VolatilityLevel=x.VolatilityLevel, VolumeChangeRate=x.VolumeChangeRate,
      MaSlopePercent=x.MaSlopePercent, Rsi=x.Rsi };
}

public sealed class RegimeOutput
{
    [ColumnName("PredictedLabel")] public uint ClusterId { get; set; }
}

public sealed class SignalInput
{
    [ColumnName("Label")] public bool Label { get; set; }
    public float PatternTypeCode { get; set; }
    public float Rsi { get; set; }
    public float BollingerPosition { get; set; }
    public float VolumeRatio { get; set; }
    public float MarketRegimeCode { get; set; }
    public float AtrPercent { get; set; }
    public float HistoricalWinRate { get; set; }
    public float RiskRewardRatio { get; set; }
    public float PriceVsLongMovingAverage { get; set; }
    public float LongTrendHistoryAvailable { get; set; }
    public static SignalInput From(MlSignalFeatureContract x, bool label) => new()
    { Label=label, PatternTypeCode=x.PatternTypeCode, Rsi=x.Rsi,
      BollingerPosition=x.BollingerPosition, VolumeRatio=x.VolumeRatio,
      MarketRegimeCode=x.MarketRegimeCode, AtrPercent=x.AtrPercent,
      HistoricalWinRate=x.HistoricalWinRate, RiskRewardRatio=x.RiskRewardRatio,
      PriceVsLongMovingAverage=x.PriceVsLongMovingAverage,
      LongTrendHistoryAvailable=x.LongTrendHistoryAvailable };
    public SignalInput Clone() => (SignalInput)MemberwiseClone();
}

public sealed class SignalOutput
{
    [ColumnName("Probability")] public float Probability { get; set; }
}

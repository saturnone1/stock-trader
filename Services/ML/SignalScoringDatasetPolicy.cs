using StockTrader.Application.MachineLearning;

namespace StockTrader.Services.ML;

internal sealed record SignalScoringDatasetSplit(
    IReadOnlyList<SignalScoringTrainingSample> Training,
    IReadOnlyList<SignalScoringTrainingSample> Validation);

/// <summary>시간 순서를 보존한 학습/검증 분할과 레이블 유효성을 소유합니다.</summary>
internal static class SignalScoringDatasetPolicy
{
    private const decimal ValidationFraction = 0.2m;

    public static bool TrySplit(
        IReadOnlyList<SignalScoringTrainingSample> samples,
        out SignalScoringDatasetSplit? split,
        out string reason)
    {
        var ordered = samples
            .Where(sample => sample.Features.SchemaVersion
                == SignalScoringFeatureSchema.CurrentVersion)
            .OrderBy(sample => sample.SignalBarAt)
            .ThenBy(sample => sample.SourceSignalId)
            .ToArray();
        var validationCount = (int)Math.Ceiling(ordered.Length * ValidationFraction);
        var trainingCount = ordered.Length - validationCount;
        if (trainingCount < 2 || validationCount < 2)
        {
            split = null;
            reason = "시간순 학습/검증 구간을 만들 샘플이 부족합니다.";
            return false;
        }

        var training = ordered[..trainingCount];
        var validation = ordered[trainingCount..];
        if (!HasBothLabels(training) || !HasBothLabels(validation))
        {
            split = null;
            reason = "학습 구간과 미래 검증 구간에 승리·손실 레이블이 모두 필요합니다.";
            return false;
        }

        split = new SignalScoringDatasetSplit(training, validation);
        reason = string.Empty;
        return true;
    }

    private static bool HasBothLabels(
        IReadOnlyCollection<SignalScoringTrainingSample> samples) =>
        samples.Any(sample => sample.IsWin)
        && samples.Any(sample => !sample.IsWin);
}

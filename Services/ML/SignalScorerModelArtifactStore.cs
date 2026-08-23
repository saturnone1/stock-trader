using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML;
using StockTrader.Application.MachineLearning;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.ML;

internal sealed record SignalScorerModelManifest(
    int FeatureSchemaVersion,
    int FeatureCount,
    DateTime TrainedAtUtc,
    int TrainingSamples,
    double ValidationAccuracy,
    double ValidationAuc,
    List<FeatureImportance> FeatureImportances,
    string ModelSha256);

internal sealed record StoredSignalScorer(
    ITransformer Model,
    SignalScorerModelManifest Manifest);

/// <summary>모델 파일과 버전·해시 manifest를 검증 가능한 한 쌍으로 관리합니다.</summary>
internal sealed class SignalScorerModelArtifactStore(
    MLContext mlContext,
    MLSettings settings,
    ILogger logger)
{
    public StoredSignalScorer? TryImport(
        StockTrader.ServiceContracts.MachineLearning.MlModelArtifactContract artifact)
    {
        var error = StockTrader.ServiceContracts.MachineLearning.MlTrainingContractPolicy
            .ArtifactError(artifact);
        if (error is not null
            || artifact.ModelKind != StockTrader.ServiceContracts.MachineLearning.MlModelKinds.SignalScorer)
            return null;
        var manifest = new SignalScorerModelManifest(
            artifact.FeatureSchemaVersion, artifact.FeatureCount,
            artifact.TrainedAtUtc, artifact.TrainingSamples,
            artifact.ValidationAccuracy ?? 0, artifact.ValidationAuc ?? 0,
            artifact.FeatureImportances.Select(x =>
                new FeatureImportance(x.FeatureName, x.Importance)).ToList(),
            artifact.ModelSha256);
        var modelPath = GetModelPath();
        var manifestPath = ManifestPath(modelPath);
        var suffix = $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(modelPath + suffix, artifact.ModelBytes);
            File.WriteAllText(manifestPath + suffix, JsonSerializer.Serialize(manifest));
            File.Move(modelPath + suffix, modelPath, true);
            File.Move(manifestPath + suffix, manifestPath, true);
            using var stream = new MemoryStream(artifact.ModelBytes, writable: false);
            return new StoredSignalScorer(mlContext.Model.Load(stream, out _), manifest);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "시그널 스코러 원격 아티팩트 가져오기 실패");
            return null;
        }
        finally
        {
            if (File.Exists(modelPath + suffix)) File.Delete(modelPath + suffix);
            if (File.Exists(manifestPath + suffix)) File.Delete(manifestPath + suffix);
        }
    }

    public bool TrySave(
        ITransformer model,
        SignalScorerModelManifest manifest,
        out SignalScorerModelManifest savedManifest)
    {
        var modelPath = GetModelPath();
        var manifestPath = ManifestPath(modelPath);
        var suffix = $".{Guid.NewGuid():N}.tmp";
        var temporaryModel = modelPath + suffix;
        var temporaryManifest = manifestPath + suffix;
        savedManifest = manifest;
        try
        {
            mlContext.Model.Save(model, null, temporaryModel);
            savedManifest = manifest with { ModelSha256 = ComputeSha256(temporaryModel) };
            File.WriteAllText(
                temporaryManifest,
                JsonSerializer.Serialize(
                    savedManifest,
                    new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryModel, modelPath, overwrite: true);
            File.Move(temporaryManifest, manifestPath, overwrite: true);
            logger.LogInformation("시그널 스코러 모델과 스키마 증거 저장 완료: {Path}", modelPath);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "시그널 스코러 모델 저장 실패");
            return false;
        }
        finally
        {
            if (File.Exists(temporaryModel)) File.Delete(temporaryModel);
            if (File.Exists(temporaryManifest)) File.Delete(temporaryManifest);
        }
    }

    public StoredSignalScorer? TryLoad()
    {
        try
        {
            var modelPath = GetModelPath();
            var manifestPath = ManifestPath(modelPath);
            if (!File.Exists(modelPath)) return null;
            if (!File.Exists(manifestPath))
            {
                logger.LogWarning(
                    "기존 시그널 스코러 모델에 인과적 피처 스키마 증거가 없어 사용하지 않음: {Path}",
                    modelPath);
                return null;
            }

            var manifest = JsonSerializer.Deserialize<SignalScorerModelManifest>(
                File.ReadAllText(manifestPath));
            if (!IsCompatible(manifest, modelPath))
            {
                logger.LogWarning(
                    "시그널 스코러 모델 스키마 또는 해시가 현재 실행과 일치하지 않아 사용하지 않음: {Path}",
                    modelPath);
                return null;
            }

            var loaded = mlContext.Model.Load(modelPath, out _);
            logger.LogInformation("시그널 스코러 모델 로드 완료: {Path}", modelPath);
            return new StoredSignalScorer(loaded, manifest!);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "저장된 시그널 스코러 모델 로드 실패 (신규 학습 필요)");
            return null;
        }
    }

    private string GetModelPath()
    {
        var directory = Path.IsPathRooted(settings.ModelDirectory)
            ? settings.ModelDirectory
            : Path.Combine(AppContext.BaseDirectory, settings.ModelDirectory);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, settings.SignalScorerModelFileName);
    }

    private static bool IsCompatible(
        SignalScorerModelManifest? manifest,
        string modelPath) =>
        manifest is not null
        && manifest.FeatureSchemaVersion == SignalScoringFeatureSchema.CurrentVersion
        && manifest.FeatureCount == SignalScoringFeatureSchema.FeatureCount
        && !string.IsNullOrWhiteSpace(manifest.ModelSha256)
        && string.Equals(
            manifest.ModelSha256,
            ComputeSha256(modelPath),
            StringComparison.OrdinalIgnoreCase);

    private static string ManifestPath(string modelPath) => modelPath + ".manifest.json";

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

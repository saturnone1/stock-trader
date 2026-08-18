using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML;
using StockTrader.Application.MachineLearning;
using StockTrader.Configuration;

namespace StockTrader.Services.ML;

internal sealed record MarketRegimeModelManifest(
    int FeatureSchemaVersion,
    int FeatureCount,
    int ClusterCount,
    DateTime TrainedAtUtc,
    int TrainingSamples,
    Dictionary<uint, string> ClusterLabels,
    string ModelSha256);

internal sealed record StoredMarketRegimeModel(
    ITransformer Model,
    MarketRegimeModelManifest Manifest);

/// <summary>레짐 모델과 의미 레이블을 하나의 해시 검증 아티팩트로 관리합니다.</summary>
internal sealed class MarketRegimeModelArtifactStore(
    MLContext mlContext,
    MLSettings settings,
    ILogger logger)
{
    public bool TrySave(
        ITransformer model,
        MarketRegimeModelManifest manifest,
        out MarketRegimeModelManifest savedManifest)
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
            logger.LogInformation("시장 레짐 모델과 의미 증거 저장 완료: {Path}", modelPath);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "시장 레짐 모델 저장 실패");
            return false;
        }
        finally
        {
            if (File.Exists(temporaryModel)) File.Delete(temporaryModel);
            if (File.Exists(temporaryManifest)) File.Delete(temporaryManifest);
        }
    }

    public StoredMarketRegimeModel? TryLoad()
    {
        try
        {
            var modelPath = GetModelPath();
            var manifestPath = ManifestPath(modelPath);
            if (!File.Exists(modelPath)) return null;
            if (!File.Exists(manifestPath))
            {
                logger.LogWarning(
                    "기존 시장 레짐 모델에 피처·클러스터 의미 증거가 없어 사용하지 않음: {Path}",
                    modelPath);
                return null;
            }

            var manifest = JsonSerializer.Deserialize<MarketRegimeModelManifest>(
                File.ReadAllText(manifestPath));
            if (!IsCompatible(manifest, modelPath))
            {
                logger.LogWarning(
                    "시장 레짐 모델 스키마, 레이블 또는 해시가 현재 실행과 일치하지 않아 사용하지 않음: {Path}",
                    modelPath);
                return null;
            }

            var model = mlContext.Model.Load(modelPath, out _);
            logger.LogInformation("검증된 시장 레짐 모델 로드 완료: {Path}", modelPath);
            return new StoredMarketRegimeModel(model, manifest!);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "저장된 시장 레짐 모델 로드 실패 (신규 학습 필요)");
            return null;
        }
    }

    private string GetModelPath()
    {
        var directory = Path.IsPathRooted(settings.ModelDirectory)
            ? settings.ModelDirectory
            : Path.Combine(AppContext.BaseDirectory, settings.ModelDirectory);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, settings.RegimeModelFileName);
    }

    private bool IsCompatible(
        MarketRegimeModelManifest? manifest,
        string modelPath) =>
        manifest is not null
        && manifest.FeatureSchemaVersion == MarketRegimeFeatureSchema.CurrentVersion
        && manifest.FeatureCount == MarketRegimeFeatureSchema.FeatureCount
        && manifest.ClusterCount == MarketRegimeClusterCatalog.RequiredClusterCount
        && manifest.TrainingSamples >= settings.MinTrainingSamples
        && HasCompleteLabels(manifest.ClusterLabels, manifest.ClusterCount)
        && !string.IsNullOrWhiteSpace(manifest.ModelSha256)
        && string.Equals(
            manifest.ModelSha256,
            ComputeSha256(modelPath),
            StringComparison.OrdinalIgnoreCase);

    private static bool HasCompleteLabels(
        IReadOnlyDictionary<uint, string>? labels,
        int clusterCount) =>
        labels is not null
        && labels.Count == clusterCount
        && labels.Keys.All(key => key > 0 && key <= clusterCount)
        && labels.Values.ToHashSet(StringComparer.Ordinal)
            .SetEquals(MarketRegimeClusterCatalog.Labels);

    private static string ManifestPath(string modelPath) => modelPath + ".manifest.json";

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

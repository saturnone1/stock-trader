using System.Text.Json;
using StockTrader.Application.Strategies;
using StockTrader.Domain.MarketData;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Optimization.Protocol;

public static class StrategyExecutionArtifactFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static StrategyExecutionArtifact Create(StrategyDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var document = JsonSerializer.Deserialize<StrategyDocument>(
            JsonSerializer.Serialize(source, JsonOptions), JsonOptions)
            ?? throw new InvalidOperationException("전략 실행 문서를 복제하지 못했습니다.");
        document.StoredStrategyId = null;
        var compilation = StrategyCompiler.Compile(document);
        if (!compilation.IsValid)
            throw new ArgumentException(string.Join(" ", compilation.Errors), nameof(source));
        var documentJson = JsonSerializer.Serialize(document, JsonOptions);

        return new StrategyExecutionArtifact(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            documentJson,
            CanonicalJsonHash.Compute(document, nameof(StrategyDocument.StoredStrategyId)),
            document.DocumentVersion,
            StrategyCompiler.CurrentSchemaVersion,
            OptimizationWorkerContractCatalog.EngineSemanticsVersion,
            OptimizationWorkerContractCatalog.IndicatorCatalogVersion,
            OptimizationWorkerContractCatalog.PatternCatalogVersion,
            MarketCalendarVersion.Current,
            OptimizationWorkerContractCatalog.OptimizationCostModelVersion);
    }
}

public static class StrategyExecutionArtifactPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? CompatibilityError(StrategyExecutionArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.ContractVersion != OptimizationWorkerContractCatalog.EvaluationInputVersion
            || artifact.CompilerSchemaVersion != StrategyCompiler.CurrentSchemaVersion
            || artifact.EngineSemanticsVersion != OptimizationWorkerContractCatalog.EngineSemanticsVersion
            || artifact.IndicatorCatalogVersion != OptimizationWorkerContractCatalog.IndicatorCatalogVersion
            || artifact.PatternCatalogVersion != OptimizationWorkerContractCatalog.PatternCatalogVersion
            || artifact.CalendarVersion != MarketCalendarVersion.Current
            || artifact.CostModelVersion != OptimizationWorkerContractCatalog.OptimizationCostModelVersion)
            return "전략 실행 계약 또는 의미 버전이 일치하지 않습니다.";

        StrategyDocument? document;
        try { document = JsonSerializer.Deserialize<StrategyDocument>(artifact.StrategyDocumentJson, JsonOptions); }
        catch (JsonException) { return "전략 실행 문서 JSON을 읽을 수 없습니다."; }
        if (document is null || document.StoredStrategyId is not null)
            return "전략 실행 문서가 없거나 저장소 식별자를 포함합니다.";
        if (document.DocumentVersion != artifact.StrategyDocumentVersion
            || !StrategyCompiler.Compile(document).IsValid)
            return "전략 실행 문서를 현재 컴파일러로 해석할 수 없습니다.";
        return CanonicalJsonHash.Compute(document, nameof(StrategyDocument.StoredStrategyId)) == artifact.ContentHash
            ? null
            : "전략 실행 아티팩트의 내용 해시가 일치하지 않습니다.";
    }
}

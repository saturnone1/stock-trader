using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StockTrader.Application.MarketData;
using StockTrader.Application.Strategies;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Application.Optimization;

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

public static class OptimizationDataEvidenceFactory
{
    public static OptimizationDataEvidenceSet Create(OptimizationEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var series = context.DataByTimeFrame.OrderBy(item => item.Key)
            .SelectMany(frame => frame.Value.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(symbol => CreateSeries(context.Request, symbol.Key, frame.Key,
                    symbol.Value.Bars, context.EvidenceFor(frame.Key))))
            .ToArray();
        return new(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            CanonicalJsonHash.Compute(series),
            series);
    }

    private static OptimizationSymbolDataEvidence CreateSeries(
        OptimizeRequest request, string symbol, TimeFrame frame,
        IReadOnlyList<OhlcvBar> bars, MarketDataEvidence evidence) => new(
            symbol.Trim().ToUpperInvariant(), frame.ToString(), evidence.Provider.ToString(),
            evidence.MarketRegion.ToString(), evidence.AdjustmentMode.ToString(),
            evidence.SessionScope.ToString(), evidence.CalendarVersion, request.From, request.To,
            bars.Count == 0 ? null : bars[0].Timestamp,
            bars.Count == 0 ? null : bars[^1].Timestamp,
            bars.Count, OptimizationDataCompleteness.Unverified, HashBars(bars));

    private static string HashBars(IReadOnlyList<OhlcvBar> bars)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var bar in bars)
        {
            var line = string.Join('|', Normalize(bar.Timestamp).Ticks,
                bar.Open.ToString(CultureInfo.InvariantCulture),
                bar.High.ToString(CultureInfo.InvariantCulture),
                bar.Low.ToString(CultureInfo.InvariantCulture),
                bar.Close.ToString(CultureInfo.InvariantCulture), bar.Volume,
                bar.Vwap?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            hash.AppendData(Encoding.UTF8.GetBytes(line));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

public static class OptimizationEvaluationInputFactory
{
    public static OptimizationEvaluationInput Create(OptimizationEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var requestJson = OptimizeRequestJsonCodec.Serialize(context.Request);
        var strategy = StrategyExecutionArtifactFactory.Create(context.Request.BasePattern);
        var evidence = OptimizationDataEvidenceFactory.Create(context);
        var hash = OptimizationEvaluationInputIdentity.Compute(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            requestJson, strategy.ContentHash, evidence.EvidenceId);
        return new(OptimizationWorkerContractCatalog.EvaluationInputVersion,
            hash, requestJson, strategy, evidence);
    }
}

/// <summary>Current in-process adapter; a later F# lease adapter implements the same port.</summary>
public interface IOptimizationWorkExecutor
{
    Task<OptimizationJobExecutionDisposition> ExecuteAsync(
        OptimizationJobExecutionTicket job,
        CancellationToken ct);
}

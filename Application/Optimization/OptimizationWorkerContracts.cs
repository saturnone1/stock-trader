using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StockTrader.Application.Contracts;
using StockTrader.Application.MarketData;
using StockTrader.Application.Strategies;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.Optimization;

/// <summary>Versions of contracts and deterministic semantics crossing the worker boundary.</summary>
public static class OptimizationWorkerContractCatalog
{
    public const int EvaluationInputVersion = 1;
    public const int LeaseVersion = 1;
    public const int ResultVersion = 1;
    public const string EngineSemanticsVersion = "long-position-session-v1";
    public const string IndicatorCatalogVersion = "indicator-catalog-v1";
    public const string PatternCatalogVersion = "pattern-catalog-v1";
    public const string OptimizationCostModelVersion = "adaptive-cost-v1";
}

public sealed record StrategyExecutionArtifact(
    int ContractVersion,
    StrategyDocument Document,
    string ContentHash,
    int CompilerSchemaVersion,
    string EngineSemanticsVersion,
    string IndicatorCatalogVersion,
    string PatternCatalogVersion,
    string CalendarVersion,
    string CostModelVersion);

public static class StrategyExecutionArtifactFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static StrategyExecutionArtifact Create(StrategyDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var document = JsonSerializer.Deserialize<StrategyDocument>(
            JsonSerializer.Serialize(source, JsonOptions),
            JsonOptions) ?? throw new InvalidOperationException("전략 실행 문서를 복제하지 못했습니다.");
        document.StoredStrategyId = null;
        var compilation = StrategyCompiler.Compile(document);
        if (!compilation.IsValid)
            throw new ArgumentException(string.Join(" ", compilation.Errors), nameof(source));

        return new StrategyExecutionArtifact(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            document,
            CanonicalJsonHash.Compute(document, nameof(StrategyDocument.StoredStrategyId)),
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
    public static string? CompatibilityError(StrategyExecutionArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.ContractVersion != OptimizationWorkerContractCatalog.EvaluationInputVersion)
            return "지원하지 않는 전략 실행 계약 버전입니다.";
        if (artifact.CompilerSchemaVersion != StrategyCompiler.CurrentSchemaVersion)
            return "전략 컴파일러 버전이 일치하지 않습니다.";
        if (artifact.EngineSemanticsVersion != OptimizationWorkerContractCatalog.EngineSemanticsVersion
            || artifact.IndicatorCatalogVersion != OptimizationWorkerContractCatalog.IndicatorCatalogVersion
            || artifact.PatternCatalogVersion != OptimizationWorkerContractCatalog.PatternCatalogVersion
            || artifact.CalendarVersion != MarketCalendarVersion.Current
            || artifact.CostModelVersion != OptimizationWorkerContractCatalog.OptimizationCostModelVersion)
            return "실행 의미 또는 카탈로그 버전이 일치하지 않습니다.";
        if (artifact.Document.StoredStrategyId is not null)
            return "전략 실행 아티팩트에 저장소 식별자가 포함되어 있습니다.";
        var compilation = StrategyCompiler.Compile(artifact.Document);
        if (!compilation.IsValid)
            return "전략 실행 아티팩트를 현재 컴파일러로 해석할 수 없습니다.";

        var expectedHash = CanonicalJsonHash.Compute(
            artifact.Document,
            nameof(StrategyDocument.StoredStrategyId));
        return string.Equals(expectedHash, artifact.ContentHash, StringComparison.Ordinal)
            ? null
            : "전략 실행 아티팩트의 내용 해시가 일치하지 않습니다.";
    }
}

public enum OptimizationDataCompleteness
{
    Unverified,
    Complete,
    Partial
}

public sealed record OptimizationSymbolDataEvidence(
    string Symbol,
    TimeFrame TimeFrame,
    DataSource Provider,
    MarketRegion Market,
    PriceAdjustmentMode AdjustmentMode,
    MarketSessionScope SessionScope,
    string CalendarVersion,
    DateTime RequestedFrom,
    DateTime RequestedTo,
    DateTime? FirstObservedBar,
    DateTime? LastObservedBar,
    int BarCount,
    OptimizationDataCompleteness Completeness,
    string ContentHash);

public sealed record OptimizationDataEvidenceSet(
    int ContractVersion,
    string EvidenceId,
    IReadOnlyList<OptimizationSymbolDataEvidence> Series);

public static class OptimizationDataEvidenceFactory
{
    public static OptimizationDataEvidenceSet Create(OptimizationEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var series = context.DataByTimeFrame
            .OrderBy(item => item.Key)
            .SelectMany(timeFrame => timeFrame.Value
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(symbol => CreateSeries(
                    context.Request,
                    symbol.Key,
                    timeFrame.Key,
                    symbol.Value.Bars,
                    context.EvidenceFor(timeFrame.Key))))
            .ToArray();

        return new OptimizationDataEvidenceSet(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            CanonicalJsonHash.Compute(series),
            series);
    }

    private static OptimizationSymbolDataEvidence CreateSeries(
        OptimizeRequest request,
        string symbol,
        TimeFrame timeFrame,
        IReadOnlyList<OhlcvBar> bars,
        MarketDataEvidence evidence) => new(
            symbol.Trim().ToUpperInvariant(),
            timeFrame,
            evidence.Provider,
            evidence.MarketRegion,
            evidence.AdjustmentMode,
            evidence.SessionScope,
            evidence.CalendarVersion,
            request.From,
            request.To,
            bars.Count == 0 ? null : bars[0].Timestamp,
            bars.Count == 0 ? null : bars[^1].Timestamp,
            bars.Count,
            // Existing providers do not yet prove gap-free completeness. The contract records
            // that limitation instead of promoting prepared data to "complete" by assumption.
            OptimizationDataCompleteness.Unverified,
            HashBars(bars));

    private static string HashBars(IReadOnlyList<OhlcvBar> bars)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var bar in bars)
        {
            var line = string.Join('|',
                NormalizeTimestamp(bar.Timestamp).Ticks.ToString(CultureInfo.InvariantCulture),
                bar.Open.ToString(CultureInfo.InvariantCulture),
                bar.High.ToString(CultureInfo.InvariantCulture),
                bar.Low.ToString(CultureInfo.InvariantCulture),
                bar.Close.ToString(CultureInfo.InvariantCulture),
                bar.Volume.ToString(CultureInfo.InvariantCulture),
                bar.Vwap?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            hash.AppendData(Encoding.UTF8.GetBytes(line));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static DateTime NormalizeTimestamp(DateTime timestamp) => timestamp.Kind switch
    {
        DateTimeKind.Local => timestamp.ToUniversalTime(),
        DateTimeKind.Utc => timestamp,
        _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
    };
}

public sealed record OptimizationEvaluationInput(
    int ContractVersion,
    string InputHash,
    string RequestJson,
    StrategyExecutionArtifact Strategy,
    OptimizationDataEvidenceSet DataEvidence);

public static class OptimizationEvaluationInputFactory
{
    public static OptimizationEvaluationInput Create(OptimizationEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var requestJson = OptimizeRequestJsonCodec.Serialize(context.Request);
        var strategy = StrategyExecutionArtifactFactory.Create(context.Request.BasePattern);
        var evidence = OptimizationDataEvidenceFactory.Create(context);
        var hash = CanonicalJsonHash.Compute(new
        {
            ContractVersion = OptimizationWorkerContractCatalog.EvaluationInputVersion,
            RequestJson = requestJson,
            StrategyHash = strategy.ContentHash,
            EvidenceId = evidence.EvidenceId
        });
        return new OptimizationEvaluationInput(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            hash,
            requestJson,
            strategy,
            evidence);
    }
}

public sealed record OptimizationWorkLease(
    int ContractVersion,
    string LeaseId,
    int JobId,
    long LeaseGeneration,
    long CancellationGeneration,
    DateTime LeasedAt,
    DateTime ExpiresAt,
    OptimizationEvaluationInput Input);

public sealed record OptimizationWorkerHeartbeat(
    int ContractVersion,
    string LeaseId,
    int JobId,
    long LeaseGeneration,
    long CancellationGeneration,
    string InputHash,
    long TestedCombinations,
    DateTime ObservedAt);

public sealed record OptimizationWorkerResultSubmission(
    int ContractVersion,
    string SubmissionId,
    string LeaseId,
    int JobId,
    long LeaseGeneration,
    long CancellationGeneration,
    string InputHash,
    string ResultHash,
    string ResultJson,
    DateTime CompletedAt);

public enum OptimizationResultAcceptance
{
    Accepted,
    Duplicate,
    UnsupportedContract,
    StaleLease,
    CancelledGeneration,
    InputMismatch,
    LeaseExpired,
    ResultHashMismatch
}

public static class OptimizationResultAcceptancePolicy
{
    public static OptimizationResultAcceptance Evaluate(
        OptimizationWorkLease activeLease,
        OptimizationWorkerResultSubmission submission,
        long currentCancellationGeneration,
        bool submissionAlreadyAccepted,
        DateTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(activeLease);
        ArgumentNullException.ThrowIfNull(submission);
        if (submission.ContractVersion != OptimizationWorkerContractCatalog.ResultVersion)
            return OptimizationResultAcceptance.UnsupportedContract;
        if (submission.JobId != activeLease.JobId
            || submission.LeaseId != activeLease.LeaseId
            || submission.LeaseGeneration != activeLease.LeaseGeneration)
            return OptimizationResultAcceptance.StaleLease;
        if (submission.CancellationGeneration != currentCancellationGeneration
            || activeLease.CancellationGeneration != currentCancellationGeneration)
            return OptimizationResultAcceptance.CancelledGeneration;
        if (submission.InputHash != activeLease.Input.InputHash)
            return OptimizationResultAcceptance.InputMismatch;
        if (observedAt > activeLease.ExpiresAt)
            return OptimizationResultAcceptance.LeaseExpired;
        if (CanonicalJsonHash.Compute(submission.ResultJson) != submission.ResultHash)
            return OptimizationResultAcceptance.ResultHashMismatch;
        if (submissionAlreadyAccepted)
            return OptimizationResultAcceptance.Duplicate;
        return OptimizationResultAcceptance.Accepted;
    }
}

/// <summary>
/// Process-neutral execution port. The current adapter runs in-process; a later extraction may
/// implement the same port with a leased remote worker without changing the scheduling loop.
/// </summary>
public interface IOptimizationWorkExecutor
{
    Task<OptimizationJobExecutionDisposition> ExecuteAsync(
        OptimizationJobExecutionTicket job,
        CancellationToken ct);
}

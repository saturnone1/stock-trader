using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StockTrader.Application.MarketData;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Optimization.Protocol;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Application.Optimization;

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
            OptimizationDataEvidenceIdentity.Compute(series),
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
        var preparedData = OptimizationPreparedDataFactory.Create(context);
        var hash = OptimizationEvaluationInputIdentity.Compute(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            requestJson, strategy.ContentHash, evidence.EvidenceId, preparedData.DataHash);
        return new(OptimizationWorkerContractCatalog.EvaluationInputVersion,
            hash, requestJson, strategy, evidence, preparedData);
    }
}

/// <summary>Current in-process adapter; a later F# lease adapter implements the same port.</summary>
public interface IOptimizationWorkExecutor
{
    Task<OptimizationJobExecutionDisposition> ExecuteAsync(
        OptimizationJobExecutionTicket job,
        CancellationToken ct);
}

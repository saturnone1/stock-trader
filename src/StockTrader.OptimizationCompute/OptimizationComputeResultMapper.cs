using System.Text.Json;
using StockTrader.Application.Optimization;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Optimization.Compute;

internal static class OptimizationComputeResultMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static OptimizationWorkerComputeResult Map(
        string inputHash,
        OptimizeResponse response) => new(
        OptimizationWorkerContractCatalog.ResultVersion,
        OptimizationWorkerContractCatalog.ShadowComputePurpose,
        inputHash,
        response.TotalCombinations,
        response.TestedCombinations,
        response.ElapsedMs,
        response.IsFrom,
        response.IsTo,
        response.OosFrom,
        response.OosTo,
        response.Results.Select(MapCandidate).ToArray());

    private static OptimizationWorkerCandidateResult MapCandidate(OptimizeResultItem item) => new(
        item.Rank,
        JsonSerializer.Serialize(item.Params, Json),
        item.TotalReturn,
        item.SortinoRatio,
        item.SharpeRatio,
        item.MaxDrawdown,
        item.WinRate,
        item.TotalTrades,
        item.ProfitFactor,
        item.CalmarRatio,
        item.AnnualizedReturn,
        item.OosTotalReturn,
        item.OosSortinoRatio,
        item.OosSharpeRatio,
        item.OosMaxDrawdown,
        item.OosWinRate,
        item.OosTotalTrades,
        item.OosProfitFactor,
        item.OosCalmarRatio,
        item.OosAnnualizedReturn);
}

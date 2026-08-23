using System.Text.Json;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Application.Optimization;

public static class OptimizationShadowResultIdentity
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static OptimizationWorkerComputeResult Normalize(
        OptimizationWorkerComputeResult result) => result with
    {
        ElapsedMs = 0,
        Results = result.Results
            .OrderBy(candidate => candidate.Rank)
            .Select(candidate => candidate with
            {
                ParametersJson = NormalizeParameters(candidate.ParametersJson)
            })
            .ToArray()
    };

    public static string Compute(OptimizationWorkerComputeResult result) =>
        CanonicalJsonHash.Compute(Normalize(result));

    public static string Serialize(OptimizationWorkerComputeResult result) =>
        JsonSerializer.Serialize(Normalize(result), Json);

    private static string NormalizeParameters(string json)
    {
        var parameters = JsonSerializer.Deserialize<OptimizeParamSnapshot>(json, Json)
            ?? throw new InvalidOperationException("Optimization parameters are empty.");
        return JsonSerializer.Serialize(parameters, Json);
    }
}

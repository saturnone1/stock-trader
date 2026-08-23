using System.Text.Json;
using System.Globalization;
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
                ParametersJson = NormalizeParameters(candidate.ParametersJson),
                TotalReturn = NormalizeDecimal(candidate.TotalReturn),
                SortinoRatio = NormalizeDecimal(candidate.SortinoRatio),
                SharpeRatio = NormalizeDecimal(candidate.SharpeRatio),
                MaxDrawdown = NormalizeDecimal(candidate.MaxDrawdown),
                WinRate = NormalizeDecimal(candidate.WinRate),
                ProfitFactor = NormalizeDecimal(candidate.ProfitFactor),
                CalmarRatio = NormalizeDecimal(candidate.CalmarRatio),
                AnnualizedReturn = NormalizeDecimal(candidate.AnnualizedReturn),
                OosTotalReturn = NormalizeDecimal(candidate.OosTotalReturn),
                OosSortinoRatio = NormalizeDecimal(candidate.OosSortinoRatio),
                OosSharpeRatio = NormalizeDecimal(candidate.OosSharpeRatio),
                OosMaxDrawdown = NormalizeDecimal(candidate.OosMaxDrawdown),
                OosWinRate = NormalizeDecimal(candidate.OosWinRate),
                OosProfitFactor = NormalizeDecimal(candidate.OosProfitFactor),
                OosCalmarRatio = NormalizeDecimal(candidate.OosCalmarRatio),
                OosAnnualizedReturn = NormalizeDecimal(candidate.OosAnnualizedReturn)
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

    private static decimal NormalizeDecimal(decimal value) => decimal.Parse(
        value.ToString("G29", CultureInfo.InvariantCulture),
        NumberStyles.Float,
        CultureInfo.InvariantCulture);

    private static decimal? NormalizeDecimal(decimal? value) =>
        value.HasValue ? NormalizeDecimal(value.Value) : null;
}

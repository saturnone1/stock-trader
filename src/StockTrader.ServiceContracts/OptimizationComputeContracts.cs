namespace StockTrader.ServiceContracts.Optimization;

public sealed record OptimizationWorkerCandidateResult(
    int Rank,
    string ParametersJson,
    decimal TotalReturn,
    decimal SortinoRatio,
    decimal SharpeRatio,
    decimal MaxDrawdown,
    decimal WinRate,
    int TotalTrades,
    decimal ProfitFactor,
    decimal CalmarRatio,
    decimal AnnualizedReturn,
    decimal? OosTotalReturn,
    decimal? OosSortinoRatio,
    decimal? OosSharpeRatio,
    decimal? OosMaxDrawdown,
    decimal? OosWinRate,
    int? OosTotalTrades,
    decimal? OosProfitFactor,
    decimal? OosCalmarRatio,
    decimal? OosAnnualizedReturn);

public sealed record OptimizationWorkerComputeResult(
    int ContractVersion,
    string Purpose,
    string InputHash,
    int TotalCombinations,
    int TestedCombinations,
    long ElapsedMs,
    DateTime? InSampleFrom,
    DateTime? InSampleTo,
    DateTime? OutOfSampleFrom,
    DateTime? OutOfSampleTo,
    IReadOnlyList<OptimizationWorkerCandidateResult> Results);

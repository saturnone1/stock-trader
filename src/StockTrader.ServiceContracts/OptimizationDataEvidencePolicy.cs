namespace StockTrader.ServiceContracts.Optimization;

public static class OptimizationDataEvidenceIdentity
{
    public static string Compute(IReadOnlyList<OptimizationSymbolDataEvidence> series) =>
        CanonicalJsonHash.Compute(series);
}

public static class OptimizationDataEvidenceCompatibilityPolicy
{
    public static string? Error(OptimizationDataEvidenceSet evidence)
    {
        if (evidence.ContractVersion != OptimizationWorkerContractCatalog.EvaluationInputVersion)
            return "unsupported-data-evidence-contract";
        if (evidence.Series.Count == 0)
            return "empty-data-evidence";
        if (evidence.Series.GroupBy(item => $"{item.TimeFrame}|{item.Symbol}",
                StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            return "duplicate-data-evidence-series";
        if (evidence.Series.Any(item => string.IsNullOrWhiteSpace(item.Symbol)
                || string.IsNullOrWhiteSpace(item.TimeFrame)
                || string.IsNullOrWhiteSpace(item.MarketTimeZoneId)
                || string.IsNullOrWhiteSpace(item.ContentHash)
                || item.BarCount < 0
                || item.WarmupCalendarDays < 0
                || item.RequiredWarmupBars < 0))
            return "invalid-data-evidence-series";

        var expected = OptimizationDataEvidenceIdentity.Compute(evidence.Series);
        return string.Equals(expected, evidence.EvidenceId, StringComparison.Ordinal)
            ? null
            : "data-evidence-hash-mismatch";
    }
}

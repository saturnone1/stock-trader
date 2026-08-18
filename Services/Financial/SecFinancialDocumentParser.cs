using System.Globalization;
using System.Text.Json;
using StockTrader.Application.Research;

namespace StockTrader.Services.Financial;

public sealed record SecFinancialFacts(
    DateTime? AsOfDate,
    decimal? RevenueCurrent,
    decimal? RevenuePrevious,
    decimal? OperatingIncomeCurrent,
    decimal? OperatingIncomePrevious,
    decimal? NetIncomeCurrent,
    decimal? NetIncomePrevious,
    decimal? Equity,
    decimal? SharesOutstanding);

/// <summary>SEC companyfacts JSON에서 연간 재무 관측값만 결정적으로 추출합니다.</summary>
public static class SecFinancialDocumentParser
{
    private static readonly string[] RevenueConcepts =
    [
        "RevenueFromContractWithCustomerExcludingAssessedTax",
        "RevenueFromContractWithCustomerIncludingAssessedTax",
        "SalesRevenueNet"
    ];

    private static readonly string[] OperatingIncomeConcepts = ["OperatingIncomeLoss"];
    private static readonly string[] NetIncomeConcepts = ["NetIncomeLoss", "ProfitLoss"];
    private static readonly string[] EquityConcepts =
    [
        "StockholdersEquity",
        "StockholdersEquityIncludingPortionAttributableToNoncontrollingInterest"
    ];
    private static readonly string[] ShareConcepts = ["EntityCommonStockSharesOutstanding"];

    public static SecFinancialFacts? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("facts", out var facts))
            return null;

        var revenue = ExtractAnnualPair(facts, "us-gaap", RevenueConcepts, "USD");
        var operatingIncome = ExtractAnnualPair(
            facts, "us-gaap", OperatingIncomeConcepts, "USD");
        var netIncome = ExtractAnnualPair(facts, "us-gaap", NetIncomeConcepts, "USD");
        var equity = ExtractLatestValue(
            facts, "us-gaap", EquityConcepts, "USD", annualOnly: true);
        var shares = ExtractLatestValue(
            facts, "dei", ShareConcepts, "shares", annualOnly: false);
        return new SecFinancialFacts(
            revenue.AsOfDate
                ?? netIncome.AsOfDate
                ?? operatingIncome.AsOfDate
                ?? equity.AsOfDate,
            revenue.Current,
            revenue.Previous,
            operatingIncome.Current,
            operatingIncome.Previous,
            netIncome.Current,
            netIncome.Previous,
            equity.Value,
            shares.Value);
    }

    private static FinancialMetricPair ExtractAnnualPair(
        JsonElement facts,
        string taxonomy,
        IEnumerable<string> concepts,
        string unitName)
    {
        foreach (var concept in concepts)
        {
            var entries = GetMetricEntries(facts, taxonomy, concept, unitName, annualOnly: true);
            if (entries.Count == 0)
                continue;

            var ordered = entries
                .GroupBy(entry => entry.End.Date)
                .Select(group => group
                    .OrderByDescending(item => item.Filed ?? DateTime.MinValue)
                    .First())
                .OrderByDescending(entry => entry.End)
                .ToArray();
            return new FinancialMetricPair(
                ordered[0].Value,
                ordered.Length > 1 ? ordered[1].Value : null,
                ordered[0].End.Date);
        }

        return new FinancialMetricPair(null, null, null);
    }

    private static FinancialMetricValue ExtractLatestValue(
        JsonElement facts,
        string taxonomy,
        IEnumerable<string> concepts,
        string unitName,
        bool annualOnly)
    {
        foreach (var concept in concepts)
        {
            var latest = GetMetricEntries(facts, taxonomy, concept, unitName, annualOnly)
                .OrderByDescending(entry => entry.End)
                .ThenByDescending(entry => entry.Filed ?? DateTime.MinValue)
                .FirstOrDefault();
            if (latest is not null)
                return new FinancialMetricValue(latest.Value, latest.End.Date);
        }

        return new FinancialMetricValue(null, null);
    }

    private static IReadOnlyList<MetricEntry> GetMetricEntries(
        JsonElement facts,
        string taxonomy,
        string concept,
        string unitName,
        bool annualOnly)
    {
        if (!facts.TryGetProperty(taxonomy, out var taxonomyNode)
            || !taxonomyNode.TryGetProperty(concept, out var conceptNode)
            || !conceptNode.TryGetProperty("units", out var unitsNode)
            || !unitsNode.TryGetProperty(unitName, out var valuesNode)
            || valuesNode.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<MetricEntry>();
        foreach (var item in valuesNode.EnumerateArray())
        {
            if (!TryReadDecimal(item, "val", out var value)
                || !TryReadDate(item, "end", out var end))
            {
                continue;
            }

            item.TryGetProperty("fp", out var fpNode);
            item.TryGetProperty("form", out var formNode);
            item.TryGetProperty("filed", out var filedNode);
            var filed = filedNode.ValueKind == JsonValueKind.String
                ? TryParseDate(filedNode.GetString())
                : null;
            var fp = fpNode.ValueKind == JsonValueKind.String ? fpNode.GetString() : null;
            var form = formNode.ValueKind == JsonValueKind.String ? formNode.GetString() : null;
            if (!annualOnly || IsAnnualEntry(fp, form))
                result.Add(new MetricEntry(value, end, filed));
        }

        return result;
    }

    private static bool IsAnnualEntry(string? fp, string? form) =>
        string.Equals(fp, "FY", StringComparison.OrdinalIgnoreCase)
        || form is "10-K" or "10-K/A" or "20-F" or "20-F/A" or "40-F" or "40-F/A";

    private static bool TryReadDecimal(
        JsonElement element,
        string propertyName,
        out decimal value)
    {
        value = 0m;
        if (!element.TryGetProperty(propertyName, out var node))
            return false;
        if (node.ValueKind == JsonValueKind.Number)
            return node.TryGetDecimal(out value);
        return node.ValueKind == JsonValueKind.String
            && decimal.TryParse(
                node.GetString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out value);
    }

    private static bool TryReadDate(
        JsonElement element,
        string propertyName,
        out DateTime value)
    {
        value = default;
        if (!element.TryGetProperty(propertyName, out var node))
            return false;
        var parsed = TryParseDate(node.GetString());
        if (!parsed.HasValue)
            return false;
        value = parsed.Value;
        return true;
    }

    private static DateTime? TryParseDate(string? raw) =>
        DateTime.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed.Date
            : null;

    private sealed record MetricEntry(decimal Value, DateTime End, DateTime? Filed);
    private sealed record FinancialMetricPair(
        decimal? Current,
        decimal? Previous,
        DateTime? AsOfDate);
    private sealed record FinancialMetricValue(decimal? Value, DateTime? AsOfDate);
}

/// <summary>SEC 재무 관측값과 시장가격을 연구용 스냅샷으로 계산합니다.</summary>
public static class SecFinancialSnapshotFactory
{
    public static FinancialSnapshotImportItem? Create(
        string symbol,
        SecFinancialFacts facts,
        decimal? storedMarketCap,
        decimal currentPrice,
        DateTime fallbackDate)
    {
        var marketCap = facts.SharesOutstanding.HasValue && currentPrice > 0
            ? currentPrice * facts.SharesOutstanding.Value
            : storedMarketCap;
        decimal? pbRatio = marketCap.HasValue && facts.Equity is > 0
            ? Math.Round(marketCap.Value / facts.Equity.Value, 4)
            : null;
        decimal? peRatio = marketCap.HasValue && facts.NetIncomeCurrent is > 0
            ? Math.Round(marketCap.Value / facts.NetIncomeCurrent.Value, 4)
            : null;
        decimal? roePercent = facts.NetIncomeCurrent.HasValue && facts.Equity is not null and not 0
            ? Math.Round(facts.NetIncomeCurrent.Value / facts.Equity.Value * 100m, 4)
            : null;
        decimal? operatingMarginPercent = facts.OperatingIncomeCurrent.HasValue
            && facts.RevenueCurrent is not null and not 0
                ? Math.Round(
                    facts.OperatingIncomeCurrent.Value / facts.RevenueCurrent.Value * 100m,
                    4)
                : null;
        if (facts.RevenueCurrent is null
            && facts.OperatingIncomeCurrent is null
            && facts.NetIncomeCurrent is null
            && peRatio is null
            && pbRatio is null
            && roePercent is null)
        {
            return null;
        }

        return new FinancialSnapshotImportItem
        {
            Symbol = symbol,
            AsOfDate = facts.AsOfDate ?? fallbackDate.Date,
            Source = SecFinancialSyncPolicy.ProviderName,
            PeRatio = peRatio,
            PbRatio = pbRatio,
            RoePercent = roePercent,
            OperatingMarginPercent = operatingMarginPercent,
            RevenueCurrent = facts.RevenueCurrent,
            RevenuePrevious = facts.RevenuePrevious,
            OperatingIncomeCurrent = facts.OperatingIncomeCurrent,
            OperatingIncomePrevious = facts.OperatingIncomePrevious,
            NetIncomeCurrent = facts.NetIncomeCurrent,
            NetIncomePrevious = facts.NetIncomePrevious,
            Notes = "External SEC sync"
        };
    }
}

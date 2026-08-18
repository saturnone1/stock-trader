using System.Text.Json;
using FluentAssertions;
using StockTrader.Services.Financial;

namespace StockTrader.Tests;

public sealed class SecFinancialDocumentParserTests
{
    [Fact]
    public void ParseSelectsAnnualFactsLatestFilingAndMostRecentTwoYears()
    {
        using var document = JsonDocument.Parse("""
        {
          "facts": {
            "us-gaap": {
              "RevenueFromContractWithCustomerExcludingAssessedTax": { "units": { "USD": [
                { "val": 90, "end": "2024-12-31", "filed": "2025-01-15", "fp": "FY", "form": "10-K" },
                { "val": 100, "end": "2025-12-31", "filed": "2026-01-10", "fp": "FY", "form": "10-K" },
                { "val": 110, "end": "2025-12-31", "filed": "2026-02-10", "fp": "FY", "form": "10-K/A" },
                { "val": 999, "end": "2026-03-31", "filed": "2026-04-10", "fp": "Q1", "form": "10-Q" }
              ] } },
              "OperatingIncomeLoss": { "units": { "USD": [
                { "val": 22, "end": "2025-12-31", "filed": "2026-02-10", "form": "10-K" }
              ] } },
              "NetIncomeLoss": { "units": { "USD": [
                { "val": 11, "end": "2025-12-31", "filed": "2026-02-10", "fp": "FY" }
              ] } },
              "StockholdersEquity": { "units": { "USD": [
                { "val": 55, "end": "2025-12-31", "filed": "2026-02-10", "fp": "FY" }
              ] } }
            },
            "dei": {
              "EntityCommonStockSharesOutstanding": { "units": { "shares": [
                { "val": 10, "end": "2026-03-31", "filed": "2026-04-10", "form": "10-Q" }
              ] } }
            }
          }
        }
        """);

        var facts = SecFinancialDocumentParser.Parse(document.RootElement);

        facts.Should().NotBeNull();
        facts!.AsOfDate.Should().Be(new DateTime(2025, 12, 31));
        facts.RevenueCurrent.Should().Be(110m);
        facts.RevenuePrevious.Should().Be(90m);
        facts.OperatingIncomeCurrent.Should().Be(22m);
        facts.NetIncomeCurrent.Should().Be(11m);
        facts.Equity.Should().Be(55m);
        facts.SharesOutstanding.Should().Be(10m);
    }

    [Fact]
    public void FactoryUsesLiveMarketCapAndCalculatesRatiosDeterministically()
    {
        var facts = new SecFinancialFacts(
            new DateTime(2025, 12, 31),
            110m,
            90m,
            22m,
            null,
            11m,
            null,
            55m,
            10m);

        var snapshot = SecFinancialSnapshotFactory.Create(
            "TQQQ", facts, storedMarketCap: 999m, currentPrice: 20m,
            fallbackDate: new DateTime(2026, 8, 18));

        snapshot.Should().NotBeNull();
        snapshot!.PeRatio.Should().Be(18.1818m);
        snapshot.PbRatio.Should().Be(3.6364m);
        snapshot.RoePercent.Should().Be(20m);
        snapshot.OperatingMarginPercent.Should().Be(20m);
        snapshot.AsOfDate.Should().Be(new DateTime(2025, 12, 31));
    }

    [Fact]
    public void FactoryReturnsNullWhenNoUsableFinancialMetricExists()
    {
        var empty = new SecFinancialFacts(null, null, null, null, null, null, null, null, null);

        SecFinancialSnapshotFactory.Create(
                "NONE", empty, null, 0m, new DateTime(2026, 8, 18))
            .Should().BeNull();
    }
}

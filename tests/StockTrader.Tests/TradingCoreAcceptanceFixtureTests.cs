using FluentAssertions;
using StockTrader.ServiceContracts.MarketData;
using StockTrader.ServiceContracts.TradingCore;
using StockTrader.TradingCore.AcceptanceFixtures;

namespace StockTrader.Tests;

public sealed class TradingCoreAcceptanceFixtureTests
{
    [Fact]
    public void Compiler_seals_every_required_isolated_scenario()
    {
        var window = Window();
        foreach (var code in TradingCoreAcceptanceScenarioCatalog.Required)
        {
            var definition = new AcceptanceScenarioDefinition(
                TradingCoreAcceptanceVersions.Current, code, Guid.NewGuid().ToString(),
                "Yahoo", "AAPL", "Raw", "US", "market-calendar-v1", 50);

            var fixture = AcceptanceScenarioCompiler.Compile(definition, window);

            TradingCoreAcceptancePolicy.FixtureError(fixture).Should().BeNull(code);
            fixture.BrokerPlan.ScenarioCode.Should().Be(code);
            fixture.Bootstrap.Snapshot.Accounts.Should().OnlyContain(account =>
                account.AccountId.StartsWith("acceptance-", StringComparison.Ordinal));
            fixture.Bootstrap.AccountConfiguration.Accounts.Should().OnlyContain(account =>
                account.AccountId.StartsWith("acceptance-", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Compiler_rejects_incomplete_or_wrong_market_data_evidence()
    {
        var definition = new AcceptanceScenarioDefinition(
            TradingCoreAcceptanceVersions.Current,
            TradingCoreAcceptanceScenarioCatalog.Required[0], Guid.NewGuid().ToString(),
            "Yahoo", "AAPL", "Raw", "US", "market-calendar-v1", 50);
        var incomplete = Window() with
        {
            Evidence = Window().Evidence with { IsComplete = false }
        };

        var action = () => AcceptanceScenarioCompiler.Compile(definition, incomplete);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*acceptance-market-data-evidence-incomplete*");
    }

    private static MarketDataExecutionWindowResponse Window()
    {
        var start = new DateTime(2026, 6, 1, 20, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, 50)
            .Select(index => new MarketDataBar("AAPL", "Daily", start.AddDays(index),
                99m, 102m, 98m, 100m, 1_000_000, 100m))
            .ToArray();
        var contentHash = MarketDataContractHash.Content(bars);
        var evidence = new MarketDataEvidenceContract(
            MarketDataContractVersions.Current,
            MarketDataContractHash.Evidence("Yahoo", "AAPL", "Daily", "Raw",
                "market-calendar-v1", 7, contentHash),
            "Yahoo", "AAPL", "Daily", "Raw", "US", "market-calendar-v1",
            bars[0].TimestampUtc, bars[^1].TimestampUtc, bars[0].TimestampUtc,
            bars[^1].TimestampUtc, 7, true, contentHash);
        return new MarketDataExecutionWindowResponse(evidence, bars, false);
    }
}

using FluentAssertions;
using StockTrader.Domain.MarketData;
using StockTrader.MarketDataService;
using StockTrader.ServiceContracts.MarketData;

namespace StockTrader.Tests;

public sealed class MarketDataExecutionEvidenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "stocktrader-market-evidence-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecutionWindowFlagsOnlyCorrectionsToTheAlreadyEvaluatedSeries()
    {
        Directory.CreateDirectory(_root);
        var store = new BarStore(Path.Combine(_root, "market-data.db"));
        await store.InitializeAsync(CancellationToken.None);
        var from = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc);
        var tqqq = new[]
        {
            Bar("TQQQ", new DateTime(2026, 8, 26, 4, 0, 0, DateTimeKind.Utc), 100m),
            Bar("TQQQ", new DateTime(2026, 8, 27, 4, 0, 0, DateTimeKind.Utc), 101m),
        };
        var first = await store.UpsertAsync(
            Upsert("seed-tqqq", from, to, tqqq), CancellationToken.None);
        var initial = await store.ReadExecutionWindowAsync(
            Window("TQQQ", from, to, 0, null), CancellationToken.None);
        initial.Evidence.IsComplete.Should().BeTrue();
        initial.PriorEvaluatedRangeCorrected.Should().BeFalse();

        var spy = new[]
        {
            Bar("SPY", tqqq[0].TimestampUtc, 500m),
            Bar("SPY", tqqq[1].TimestampUtc, 501m),
        };
        await store.UpsertAsync(Upsert("seed-spy", from, to, spy), CancellationToken.None);
        var unrelatedCorrection = spy.ToArray();
        unrelatedCorrection[0] = Bar("SPY", spy[0].TimestampUtc, 499m);
        await store.UpsertAsync(
            Upsert("correct-spy", from, to, unrelatedCorrection), CancellationToken.None);
        var unrelated = await store.ReadExecutionWindowAsync(
            Window("TQQQ", from, to, first.Revision, tqqq[1].TimestampUtc),
            CancellationToken.None);
        unrelated.PriorEvaluatedRangeCorrected.Should().BeFalse();

        var corrected = tqqq.ToArray();
        corrected[0] = Bar("TQQQ", tqqq[0].TimestampUtc, 99m);
        await store.UpsertAsync(
            Upsert("correct-tqqq", from, to, corrected), CancellationToken.None);
        var result = await store.ReadExecutionWindowAsync(
            Window("TQQQ", from, to, first.Revision, tqqq[1].TimestampUtc),
            CancellationToken.None);

        result.PriorEvaluatedRangeCorrected.Should().BeTrue();
    }

    private static MarketDataExecutionWindowRequest Window(
        string symbol, DateTime from, DateTime to, long afterRevision, DateTime? evaluated) =>
        new(MarketDataContractVersions.Current, "Alpaca", symbol, "Daily",
            PriceAdjustmentMode.SplitsAndDividends.ToString(), "미국",
            MarketCalendarVersion.Current, from, to, 2, new DateOnly(2026, 8, 27),
            afterRevision, evaluated);

    private static MarketDataUpsertRequest Upsert(
        string id, DateTime from, DateTime to, IReadOnlyList<MarketDataBar> bars) =>
        new(MarketDataContractVersions.Current, id, "Alpaca",
            PriceAdjustmentMode.SplitsAndDividends.ToString(), "미국",
            MarketCalendarVersion.Current, from, to, true, bars);

    private static MarketDataBar Bar(string symbol, DateTime timestamp, decimal close) =>
        new(symbol, "Daily", timestamp, close, close + 1m, close - 1m, close, 1_000, null);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

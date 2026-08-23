using FluentAssertions;
using StockTrader.MarketDataService;
using StockTrader.ServiceContracts.MarketData;

namespace StockTrader.MarketDataService.Tests;

public sealed class BarStoreTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "stocktrader-market-data-tests", Guid.NewGuid().ToString("N"));
    private BarStore _store = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        _store = new BarStore(Path.Combine(_directory, "marketdata.db"));
        await _store.InitializeAsync(CancellationToken.None);
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Repeating_request_is_idempotent_and_preserves_decimal_identity()
    {
        var request = Request("request-1", Bar(10.0m));

        var first = await _store.UpsertAsync(request, CancellationToken.None);
        var second = await _store.UpsertAsync(request, CancellationToken.None);
        var range = await _store.ReadRangeAsync(Range(), CancellationToken.None);

        first.Inserted.Should().Be(1);
        second.AlreadyApplied.Should().BeTrue();
        second.Revision.Should().Be(first.Revision);
        range.Bars.Should().ContainSingle().Which.Close.Should().Be(10m);
        range.Evidence.ContentHash.Should().Be(MarketDataContractHash.Content(range.Bars));
        range.Evidence.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Changed_bar_creates_one_monotonic_correction()
    {
        var first = await _store.UpsertAsync(
            Request("request-1", Bar(10m)), CancellationToken.None);
        var changed = await _store.UpsertAsync(
            Request("request-2", Bar(11m)), CancellationToken.None);
        var corrections = await _store.CorrectionsAsync(0, 100, CancellationToken.None);

        changed.Corrected.Should().Be(1);
        changed.Revision.Should().Be(first.Revision + 1);
        corrections.Corrections.Should().ContainSingle();
        corrections.Corrections[0].Revision.Should().Be(changed.Revision);
        corrections.Corrections[0].PreviousHash.Should().NotBe(
            corrections.Corrections[0].CurrentHash);
    }

    [Fact]
    public async Task Same_bar_under_different_provider_has_distinct_identity()
    {
        await _store.UpsertAsync(Request("alpaca", Bar(10m)), CancellationToken.None);
        await _store.UpsertAsync(Request("yahoo", Bar(10m), "Yahoo"), CancellationToken.None);

        var series = await _store.SeriesAsync(CancellationToken.None);

        series.Series.Should().HaveCount(2);
        series.Series.Select(item => item.Provider).Should().BeEquivalentTo("Alpaca", "Yahoo");
    }

    [Fact]
    public async Task Concurrent_duplicate_request_is_applied_exactly_once()
    {
        var request = Request("concurrent", Bar(10m));

        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => _store.UpsertAsync(request, CancellationToken.None)));

        results.Count(result => !result.AlreadyApplied).Should().Be(1);
        results.Count(result => result.AlreadyApplied).Should().Be(11);
        (await _store.SeriesAsync(CancellationToken.None)).Series
            .Should().ContainSingle().Which.BarCount.Should().Be(1);
    }

    [Fact]
    public async Task Bar_outside_declared_evidence_range_is_rejected()
    {
        var request = Request("outside", Bar(10m) with
        {
            TimestampUtc = new DateTime(2025, 1, 4, 0, 0, 0, DateTimeKind.Utc)
        });

        var action = () => _store.UpsertAsync(request, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*declared requested range*");
    }

    private static MarketDataBar Bar(decimal close) => new(
        "TQQQ", "Daily", new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        9m, 12m, 8m, close, 1000, null);

    private static MarketDataUpsertRequest Request(
        string id, MarketDataBar bar, string provider = "Alpaca") => new(
        MarketDataContractVersions.Current, id, provider, "SplitsAndDividends", "미국", "2026.1",
        new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc), true, [bar]);

    private static MarketDataRangeRequest Range() => new(
        MarketDataContractVersions.Current, "Alpaca", "TQQQ", "Daily",
        "SplitsAndDividends", "미국", "2026.1",
        new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc));
}

using FluentAssertions;
using Moq;
using StockTrader.Application.Research;

namespace StockTrader.Tests;

public sealed class FinancialSnapshotImportServiceTests
{
    [Fact]
    public async Task UpsertNormalizesIdentityDefaultsAndTimeBeforePersistence()
    {
        var now = new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero);
        IReadOnlyList<ManagedFinancialSnapshot>? captured = null;
        var store = new Mock<IResearchUniverseStore>();
        store.Setup(item => item.UpsertFinancialSnapshotsAsync(
                It.IsAny<IReadOnlyList<ManagedFinancialSnapshot>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ManagedFinancialSnapshot>, CancellationToken>(
                (items, _) => captured = items)
            .ReturnsAsync(new FinancialImportSummary(2));
        var service = new FinancialSnapshotImportService(
            store.Object,
            new FixedTimeProvider(now));

        var result = await service.UpsertAsync(
        [
            new FinancialSnapshotImportItem
            {
                Symbol = " aapl ",
                Source = " ",
                Notes = " note "
            },
            new FinancialSnapshotImportItem
            {
                Symbol = "AAPL",
                Source = "Replacement",
                PeRatio = 12m
            },
            new FinancialSnapshotImportItem
            {
                Symbol = " msft ",
                Source = " ",
                Notes = " note "
            },
            new FinancialSnapshotImportItem { Symbol = " " }
        ]);

        result.Should().Be(new FinancialImportSummary(2));
        captured.Should().NotBeNull().And.HaveCount(2);
        var persisted = captured ?? throw new InvalidOperationException("Snapshots were not persisted.");
        var replacement = persisted.Single(item => item.Symbol == "AAPL");
        replacement.Source.Should().Be("Replacement");
        replacement.PeRatio.Should().Be(12m);
        var defaulted = persisted.Single(item => item.Symbol == "MSFT");
        defaulted.AsOfDate.Should().Be(now.UtcDateTime.Date);
        defaulted.Source.Should().Be("Manual");
        defaulted.Notes.Should().Be("note");
        defaulted.ModifiedAt.Should().Be(now.UtcDateTime);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Research;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

/// <summary>연구 종목군·재무 팩터 애플리케이션 경계의 EF Core 어댑터입니다.</summary>
public sealed class ResearchUniverseStore(IDbContextFactory<AppDbContext> dbFactory)
    : IResearchUniverseStore
{
    public async Task<IReadOnlyList<ResearchTickerSnapshot>> LoadActiveTickersAsync(
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Tickers
            .AsNoTracking()
            .Where(ticker => ticker.IsActive)
            .Select(ticker => new ResearchTickerSnapshot(
                ticker.Symbol,
                ticker.Name,
                ticker.Sector,
                ticker.Industry,
                ticker.MarketCap))
            .ToListAsync(ct);
    }

    public async Task<FinancialResearchDataSet> LoadFinancialResearchDataAsync(
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var snapshots = await db.FinancialSnapshots
            .AsNoTracking()
            .Select(snapshot => new ResearchFinancialSnapshot(
                snapshot.Id,
                snapshot.Symbol,
                snapshot.AsOfDate,
                snapshot.Source,
                snapshot.PeRatio,
                snapshot.PbRatio,
                snapshot.RoePercent,
                snapshot.OperatingMarginPercent,
                snapshot.RevenueCurrent,
                snapshot.RevenuePrevious,
                snapshot.OperatingIncomeCurrent,
                snapshot.OperatingIncomePrevious,
                snapshot.NetIncomeCurrent,
                snapshot.NetIncomePrevious,
                snapshot.UpdatedAt))
            .ToListAsync(ct);
        var tickers = await db.Tickers
            .AsNoTracking()
            .Where(ticker => ticker.IsActive)
            .Select(ticker => new ResearchTickerSnapshot(
                ticker.Symbol,
                ticker.Name,
                ticker.Sector,
                ticker.Industry,
                ticker.MarketCap))
            .ToListAsync(ct);
        return new FinancialResearchDataSet(snapshots, tickers);
    }

    public async Task<FinancialImportRunHistory> LoadImportRunHistoryAsync(
        int recentLimit,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var limit = Math.Max(1, recentLimit);
        var recent = await db.FinancialImportRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Take(limit)
            .ToListAsync(ct);
        var latestSuccess = recent.FirstOrDefault(IsSuccessful)
            ?? await db.FinancialImportRuns
                .AsNoTracking()
                .Where(run => run.Status == "Completed" && run.ImportedCount > 0)
                .OrderByDescending(run => run.CompletedAt)
                .ThenByDescending(run => run.Id)
                .FirstOrDefaultAsync(ct);
        return new FinancialImportRunHistory(
            recent.Select(ToSnapshot).ToArray(),
            latestSuccess is null ? null : ToSnapshot(latestSuccess));
    }

    public async Task<FinancialImportSummary> UpsertFinancialSnapshotsAsync(
        IReadOnlyList<ManagedFinancialSnapshot> snapshots,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var importedCount = 0;
        var uniqueSnapshots = snapshots
            .GroupBy(snapshot => (snapshot.Symbol, snapshot.AsOfDate))
            .Select(group => group.Last());
        foreach (var snapshot in uniqueSnapshots)
        {
            var existing = await db.FinancialSnapshots.FirstOrDefaultAsync(
                item => item.Symbol == snapshot.Symbol && item.AsOfDate == snapshot.AsOfDate,
                ct);
            if (existing is null)
            {
                existing = new FinancialSnapshot
                {
                    Symbol = snapshot.Symbol,
                    AsOfDate = snapshot.AsOfDate,
                    CreatedAt = snapshot.ModifiedAt
                };
                db.FinancialSnapshots.Add(existing);
            }

            Apply(snapshot, existing);
            importedCount++;
        }

        await db.SaveChangesAsync(ct);
        return new FinancialImportSummary(importedCount);
    }

    private static bool IsSuccessful(FinancialImportRun run) =>
        run.Status == "Completed" && run.ImportedCount > 0;

    private static FinancialImportRunSnapshot ToSnapshot(FinancialImportRun run) => new(
        run.Id,
        run.SourceType,
        run.FilePath,
        run.Status,
        run.ImportedCount,
        run.SkippedCount,
        run.ErrorMessage,
        run.StartedAt,
        run.CompletedAt);

    private static void Apply(ManagedFinancialSnapshot source, FinancialSnapshot target)
    {
        target.Source = source.Source;
        target.PeRatio = source.PeRatio;
        target.PbRatio = source.PbRatio;
        target.RoePercent = source.RoePercent;
        target.OperatingMarginPercent = source.OperatingMarginPercent;
        target.RevenueCurrent = source.RevenueCurrent;
        target.RevenuePrevious = source.RevenuePrevious;
        target.OperatingIncomeCurrent = source.OperatingIncomeCurrent;
        target.OperatingIncomePrevious = source.OperatingIncomePrevious;
        target.NetIncomeCurrent = source.NetIncomeCurrent;
        target.NetIncomePrevious = source.NetIncomePrevious;
        target.Notes = source.Notes;
        target.UpdatedAt = source.ModifiedAt;
    }
}

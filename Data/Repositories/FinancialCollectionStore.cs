using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Research;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

/// <summary>재무 파일·SEC 수집 실행 상태와 종목 선택을 영속화하는 EF Core 어댑터입니다.</summary>
public sealed class FinancialCollectionStore(IDbContextFactory<AppDbContext> dbFactory)
    : IFinancialCollectionStore
{
    public async Task<bool> HasCompletedRunAsync(
        string filePath,
        string fingerprint,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.FinancialImportRuns.AsNoTracking().AnyAsync(
            run => run.FilePath == filePath
                && run.Fingerprint == fingerprint
                && run.Status == "Completed",
            ct);
    }

    public async Task<long> StartOrRestartRunAsync(
        string sourceType,
        string filePath,
        string fingerprint,
        DateTime startedAt,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var run = await db.FinancialImportRuns.FirstOrDefaultAsync(
            item => item.FilePath == filePath && item.Fingerprint == fingerprint,
            ct);
        if (run is null)
        {
            run = new FinancialImportRun
            {
                FilePath = filePath,
                Fingerprint = fingerprint
            };
            db.FinancialImportRuns.Add(run);
        }

        run.SourceType = sourceType;
        run.Status = "Running";
        run.ErrorMessage = null;
        run.ImportedCount = 0;
        run.SkippedCount = 0;
        run.StartedAt = startedAt;
        run.CompletedAt = null;
        await db.SaveChangesAsync(ct);
        return run.Id;
    }

    public Task CompleteRunAsync(
        long runId,
        int importedCount,
        int skippedCount,
        string? warning,
        DateTime completedAt,
        CancellationToken ct = default) =>
        FinishRunAsync(
            runId,
            "Completed",
            importedCount,
            skippedCount,
            warning,
            completedAt,
            ct);

    public Task FailRunAsync(
        long runId,
        string error,
        DateTime completedAt,
        CancellationToken ct = default) =>
        FinishRunAsync(runId, "Failed", 0, 0, error, completedAt, ct);

    public async Task<DateTime?> GetLatestCompletedAtAsync(
        string sourceType,
        bool requireImportedItems,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.FinancialImportRuns
            .AsNoTracking()
            .Where(run => run.SourceType == sourceType && run.Status == "Completed");
        if (requireImportedItems)
            query = query.Where(run => run.ImportedCount > 0);
        return await query
            .OrderByDescending(run => run.CompletedAt)
            .ThenByDescending(run => run.Id)
            .Select(run => run.CompletedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ResearchTickerSnapshot>> LoadTopActiveTickersAsync(
        int limit,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Tickers
            .AsNoTracking()
            .Where(ticker => ticker.IsActive)
            .OrderByDescending(ticker => ticker.MarketCap)
            .ThenBy(ticker => ticker.Symbol)
            .Take(Math.Max(1, limit))
            .Select(ToSnapshotExpression())
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, ResearchTickerSnapshot>> LoadTickersAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken ct = default)
    {
        if (symbols.Count == 0)
            return new Dictionary<string, ResearchTickerSnapshot>(StringComparer.OrdinalIgnoreCase);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var symbolKeys = symbols
            .Select(symbol => symbol.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var tickers = await db.Tickers
            .AsNoTracking()
            .Where(ticker => symbolKeys.Contains(ticker.Symbol.ToUpper()))
            .Select(ToSnapshotExpression())
            .ToListAsync(ct);
        return tickers.ToDictionary(
            ticker => ticker.Symbol,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task FinishRunAsync(
        long runId,
        string status,
        int importedCount,
        int skippedCount,
        string? message,
        DateTime completedAt,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var run = await db.FinancialImportRuns.SingleAsync(item => item.Id == runId, ct);
        run.Status = status;
        run.ImportedCount = importedCount;
        run.SkippedCount = skippedCount;
        run.ErrorMessage = message;
        run.CompletedAt = completedAt;
        await db.SaveChangesAsync(ct);
    }

    private static System.Linq.Expressions.Expression<Func<Ticker, ResearchTickerSnapshot>>
        ToSnapshotExpression() =>
        ticker => new ResearchTickerSnapshot(
            ticker.Symbol,
            ticker.Name,
            ticker.Sector,
            ticker.Industry,
            ticker.MarketCap);
}

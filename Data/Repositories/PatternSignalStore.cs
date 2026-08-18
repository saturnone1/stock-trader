using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StockTrader.Application.Trading;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public sealed class PatternSignalStore(
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryCache cache)
    : IPatternSignalStore
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<List<PatternSignal>> GetActionableSignalsAsync(
        DateTime detectedFromInclusiveUtc,
        DateTime detectedThroughInclusiveUtc,
        CancellationToken ct = default)
    {
        if (detectedFromInclusiveUtc > detectedThroughInclusiveUtc)
            throw new ArgumentException("Signal observation window is inverted.");

        if (cache.TryGetValue(TradeReadCache.ActiveSignals, out List<PatternSignal>? cached)
            && cached is not null)
        {
            return FilterToWindow(
                cached,
                detectedFromInclusiveUtc,
                detectedThroughInclusiveUtc);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var signals = await db.PatternSignals
            .AsNoTracking()
            .Where(signal => signal.IsActive && !signal.IsSuperseded)
            .OrderByDescending(signal => signal.DetectedAt)
            .ToListAsync(ct);
        cache.Set(TradeReadCache.ActiveSignals, signals, CacheTtl);
        return FilterToWindow(
            signals,
            detectedFromInclusiveUtc,
            detectedThroughInclusiveUtc);
    }

    public async Task AddSignalsBatchAsync(
        IEnumerable<PatternSignal> signals,
        CancellationToken ct = default)
    {
        var candidates = signals.ToList();
        if (candidates.Count == 0)
            return;
        if (candidates.Any(signal => signal.SignalBarAt is null))
        {
            throw new InvalidOperationException(
                "Persisted pattern signals require a signal bar timestamp.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var patternTypes = candidates.Select(signal => signal.PatternType).Distinct().ToList();
        var barTimes = candidates.Select(signal => signal.SignalBarAt).Distinct().ToList();
        var existingSignals = await db.PatternSignals
            .AsNoTracking()
            .Where(signal => patternTypes.Contains(signal.PatternType)
                && barTimes.Contains(signal.SignalBarAt))
            .Select(signal => new
            {
                signal.Id,
                signal.Symbol,
                signal.PatternType,
                signal.CustomPatternName,
                signal.SignalBarAt,
            })
            .ToListAsync(ct);
        var persistedIds = existingSignals.ToDictionary(
            signal => Identity(
                signal.Symbol,
                signal.PatternType,
                signal.CustomPatternName,
                signal.SignalBarAt!.Value),
            signal => signal.Id,
            StringComparer.Ordinal);
        var identities = persistedIds.Keys.ToHashSet(StringComparer.Ordinal);
        var newSignals = candidates
            .Where(signal => identities.Add(Identity(signal)))
            .ToList();
        if (newSignals.Count > 0)
        {
            db.PatternSignals.AddRange(newSignals);
            await db.SaveChangesAsync(ct);
            foreach (var signal in newSignals)
                persistedIds[Identity(signal)] = signal.Id;
            cache.Remove(TradeReadCache.ActiveSignals);
        }

        foreach (var signal in candidates.Where(signal => signal.Id <= 0))
        {
            if (persistedIds.TryGetValue(Identity(signal), out var id))
                signal.Id = id;
        }
    }

    private static string Identity(
        string symbol,
        PatternType patternType,
        string? customPatternName,
        DateTime signalBarAt) => string.Join(
            '\u001f',
            symbol.Trim().ToUpperInvariant(),
            (int)patternType,
            customPatternName?.Trim().ToUpperInvariant() ?? string.Empty,
            signalBarAt.Ticks);

    private static string Identity(PatternSignal signal) => Identity(
        signal.Symbol,
        signal.PatternType,
        signal.CustomPatternName,
        signal.SignalBarAt!.Value);

    private static List<PatternSignal> FilterToWindow(
        IEnumerable<PatternSignal> signals,
        DateTime detectedFromInclusiveUtc,
        DateTime detectedThroughInclusiveUtc) => signals
        .Where(signal => signal.DetectedAt >= detectedFromInclusiveUtc
            && signal.DetectedAt <= detectedThroughInclusiveUtc)
        .ToList();
}

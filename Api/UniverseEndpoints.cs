using Microsoft.EntityFrameworkCore;
using StockTrader.Data;

namespace StockTrader.Api;

public static class UniverseEndpoints
{
    public static RouteGroupBuilder MapUniverseApi(this RouteGroupBuilder group)
    {
        group.MapGet("/universe/meta", async (AppDbContext db, CancellationToken ct) =>
        {
            var tickers = await db.Tickers
                .AsNoTracking()
                .Where(t => t.IsActive)
                .ToListAsync(ct);

            var withMarketCap = tickers.Where(t => t.MarketCap > 0).ToList();

            return Results.Ok(new
            {
                totalActive = tickers.Count,
                marketCapCoverage = withMarketCap.Count,
                sectors = tickers
                    .Where(t => !string.IsNullOrWhiteSpace(t.Sector))
                    .GroupBy(t => t.Sector.Trim())
                    .Select(g => new { name = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ThenBy(x => x.name)
                    .Take(20)
                    .ToList(),
                industries = tickers
                    .Where(t => !string.IsNullOrWhiteSpace(t.Industry))
                    .GroupBy(t => t.Industry.Trim())
                    .Select(g => new { name = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ThenBy(x => x.name)
                    .Take(30)
                    .ToList()
            });
        }).RequireAuthorization();

        group.MapGet("/universe/query", async (
            string? search,
            string? sectors,
            string? industries,
            decimal? marketCapMin,
            decimal? marketCapMax,
            decimal? percentileMin,
            decimal? percentileMax,
            int? limit,
            string? sortBy,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var requestedLimit = Math.Clamp(limit ?? 20, 1, 100);
            var sectorSet = ParseCsv(sectors);
            var industrySet = ParseCsv(industries);

            var tickers = await db.Tickers
                .AsNoTracking()
                .Where(t => t.IsActive && t.MarketCap > 0)
                .OrderBy(t => t.MarketCap)
                .ThenBy(t => t.Symbol)
                .ToListAsync(ct);

            if (tickers.Count == 0)
            {
                return Results.Ok(new
                {
                    totalUniverse = 0,
                    matched = 0,
                    items = Array.Empty<object>()
                });
            }

            var ranked = tickers
                .Select((ticker, index) => new UniverseTickerRow
                {
                    Symbol = ticker.Symbol,
                    Name = ticker.Name,
                    Sector = ticker.Sector,
                    Industry = ticker.Industry,
                    MarketCap = ticker.MarketCap,
                    MarketCapPercentile = tickers.Count == 1
                        ? 100m
                        : Math.Round(index * 100m / (tickers.Count - 1), 2)
                });

            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalized = search.Trim();
                ranked = ranked.Where(item =>
                    item.Symbol.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    item.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    item.Sector.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    item.Industry.Contains(normalized, StringComparison.OrdinalIgnoreCase));
            }

            if (sectorSet.Count > 0)
            {
                ranked = ranked.Where(item => sectorSet.Contains(item.Sector.Trim(), StringComparer.OrdinalIgnoreCase));
            }

            if (industrySet.Count > 0)
            {
                ranked = ranked.Where(item => industrySet.Contains(item.Industry.Trim(), StringComparer.OrdinalIgnoreCase));
            }

            if (marketCapMin.HasValue)
            {
                ranked = ranked.Where(item => item.MarketCap >= marketCapMin.Value);
            }

            if (marketCapMax.HasValue)
            {
                ranked = ranked.Where(item => item.MarketCap <= marketCapMax.Value);
            }

            if (percentileMin.HasValue)
            {
                ranked = ranked.Where(item => item.MarketCapPercentile >= percentileMin.Value);
            }

            if (percentileMax.HasValue)
            {
                ranked = ranked.Where(item => item.MarketCapPercentile <= percentileMax.Value);
            }

            ranked = (sortBy ?? "marketCapAsc").ToLowerInvariant() switch
            {
                "marketcapdesc" => ranked.OrderByDescending(item => item.MarketCap).ThenBy(item => item.Symbol),
                "symbol" => ranked.OrderBy(item => item.Symbol),
                _ => ranked.OrderBy(item => item.MarketCap).ThenBy(item => item.Symbol)
            };

            var items = ranked.Take(requestedLimit).ToList();
            var matched = ranked.Count();

            return Results.Ok(new
            {
                totalUniverse = tickers.Count,
                matched,
                items
            });
        }).RequireAuthorization();

        return group;
    }

    private static HashSet<string> ParseCsv(string? raw)
    {
        return raw?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class UniverseTickerRow
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Sector { get; set; } = string.Empty;
        public string Industry { get; set; } = string.Empty;
        public decimal MarketCap { get; set; }
        public decimal MarketCapPercentile { get; set; }
    }
}

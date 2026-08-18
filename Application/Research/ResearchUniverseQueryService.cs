namespace StockTrader.Application.Research;

public sealed record ResearchFacet(string Name, int Count);

public sealed record ResearchUniverseMeta(
    int TotalActive,
    int MarketCapCoverage,
    IReadOnlyList<ResearchFacet> Sectors,
    IReadOnlyList<ResearchFacet> Industries);

public sealed record ResearchUniverseQuery(
    string? Search = null,
    string? Sectors = null,
    string? Industries = null,
    decimal? MarketCapMin = null,
    decimal? MarketCapMax = null,
    decimal? PercentileMin = null,
    decimal? PercentileMax = null,
    int? Limit = null,
    string? SortBy = null);

public sealed record ResearchUniverseRow(
    string Symbol,
    string Name,
    string Sector,
    string Industry,
    decimal MarketCap,
    decimal MarketCapPercentile);

public sealed record ResearchUniverseResult(
    int TotalUniverse,
    int Matched,
    IReadOnlyList<ResearchUniverseRow> Items);

public sealed class ResearchUniverseQueryService(IResearchUniverseStore store)
{
    public async Task<ResearchUniverseMeta> GetMetaAsync(CancellationToken ct = default)
    {
        var tickers = await store.LoadActiveTickersAsync(ct);
        return new ResearchUniverseMeta(
            tickers.Count,
            tickers.Count(ticker => ticker.MarketCap > 0),
            BuildFacets(
                tickers.Select(ticker => ticker.Sector),
                ResearchUniversePolicy.SectorFacetLimit),
            BuildFacets(
                tickers.Select(ticker => ticker.Industry),
                ResearchUniversePolicy.IndustryFacetLimit));
    }

    public async Task<ResearchUniverseResult> QueryAsync(
        ResearchUniverseQuery request,
        CancellationToken ct = default)
    {
        var requestedLimit = Math.Clamp(
            request.Limit ?? ResearchUniversePolicy.DefaultQueryLimit,
            1,
            ResearchUniversePolicy.MaximumUniverseQueryLimit);
        var sectorSet = ResearchFilterPolicy.ParseCsv(request.Sectors);
        var industrySet = ResearchFilterPolicy.ParseCsv(request.Industries);
        var tickers = (await store.LoadActiveTickersAsync(ct))
            .Where(ticker => ticker.MarketCap > 0)
            .OrderBy(ticker => ticker.MarketCap)
            .ThenBy(ticker => ticker.Symbol)
            .ToArray();

        if (tickers.Length == 0)
            return new ResearchUniverseResult(0, 0, []);

        IEnumerable<ResearchUniverseRow> ranked = tickers.Select((ticker, index) => new ResearchUniverseRow(
            ticker.Symbol,
            ticker.Name,
            ticker.Sector,
            ticker.Industry,
            ticker.MarketCap,
            tickers.Length == 1
                ? 100m
                : Math.Round(index * 100m / (tickers.Length - 1), 2)));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var normalized = request.Search.Trim();
            ranked = ranked.Where(item =>
                item.Symbol.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || item.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || item.Sector.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || item.Industry.Contains(normalized, StringComparison.OrdinalIgnoreCase));
        }

        if (sectorSet.Count > 0)
            ranked = ranked.Where(item => sectorSet.Contains(item.Sector.Trim()));
        if (industrySet.Count > 0)
            ranked = ranked.Where(item => industrySet.Contains(item.Industry.Trim()));
        if (request.MarketCapMin.HasValue)
            ranked = ranked.Where(item => item.MarketCap >= request.MarketCapMin.Value);
        if (request.MarketCapMax.HasValue)
            ranked = ranked.Where(item => item.MarketCap <= request.MarketCapMax.Value);
        if (request.PercentileMin.HasValue)
            ranked = ranked.Where(item => item.MarketCapPercentile >= request.PercentileMin.Value);
        if (request.PercentileMax.HasValue)
            ranked = ranked.Where(item => item.MarketCapPercentile <= request.PercentileMax.Value);

        ranked = (request.SortBy ?? "marketCapAsc").ToLowerInvariant() switch
        {
            "marketcapdesc" => ranked
                .OrderByDescending(item => item.MarketCap)
                .ThenBy(item => item.Symbol),
            "symbol" => ranked.OrderBy(item => item.Symbol),
            _ => ranked
                .OrderBy(item => item.MarketCap)
                .ThenBy(item => item.Symbol)
        };

        var matches = ranked.ToArray();
        return new ResearchUniverseResult(
            tickers.Length,
            matches.Length,
            matches.Take(requestedLimit).ToArray());
    }

    private static IReadOnlyList<ResearchFacet> BuildFacets(
        IEnumerable<string> values,
        int limit) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value.Trim())
            .Select(group => new ResearchFacet(group.Key, group.Count()))
            .OrderByDescending(facet => facet.Count)
            .ThenBy(facet => facet.Name)
            .Take(limit)
            .ToArray();
}

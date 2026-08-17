using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Strategies;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public sealed class CompiledStrategyRepository : ICompiledStrategyRepository
{
    private readonly AppDbContext _db;
    private readonly ILogger<CompiledStrategyRepository> _logger;

    public CompiledStrategyRepository(AppDbContext db, ILogger<CompiledStrategyRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CompiledStrategy>> ListAsync(
        bool activeOnly = false,
        bool liveOnly = false,
        CancellationToken ct = default)
    {
        IQueryable<CustomPatternDefinition> query = _db.CustomPatterns.AsNoTracking();
        if (activeOnly) query = query.Where(pattern => pattern.IsActive);
        if (liveOnly) query = query.Where(pattern => pattern.EnableLiveTrading);

        return Compile(await query.OrderBy(pattern => pattern.Id).ToListAsync(ct));
    }

    public async Task<IReadOnlyDictionary<string, CompiledStrategy>> GetByNamesAsync(
        IEnumerable<string> names,
        CancellationToken ct = default)
    {
        var requested = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requested.Length == 0)
            return new Dictionary<string, CompiledStrategy>(StringComparer.OrdinalIgnoreCase);

        var definitions = await _db.CustomPatterns.AsNoTracking()
            .Where(pattern => requested.Contains(pattern.Name))
            .OrderBy(pattern => pattern.Id)
            .ToListAsync(ct);

        return Compile(definitions).ToDictionary(strategy => strategy.Name, StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<CompiledStrategy> Compile(IEnumerable<CustomPatternDefinition> definitions)
    {
        var strategies = new List<CompiledStrategy>();
        foreach (var definition in definitions)
        {
            var result = StrategyCompiler.Compile(definition.ToStoredStrategy().Document);
            if (result.Strategy is not null)
            {
                strategies.Add(result.Strategy);
                continue;
            }

            _logger.LogWarning(
                "Stored strategy {StrategyId}/{StrategyName} was excluded from execution: {Errors}",
                definition.Id,
                definition.Name,
                string.Join(" | ", result.Errors));
        }
        return strategies;
    }
}

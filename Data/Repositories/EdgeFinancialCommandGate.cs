using Microsoft.EntityFrameworkCore;
using StockTrader.Application.TradingCore;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Data.Repositories;

public sealed class EdgeFinancialCommandGate(
    IDbContextFactory<AppDbContext> dbFactory) : IFinancialCommandGate
{
    public async Task EnsureOpenAsync(string commandClass, CancellationToken ct = default)
    {
        if (commandClass is not FinancialCommandClasses.NewEntry
            and not FinancialCommandClasses.ManualCommand)
            throw new ArgumentOutOfRangeException(nameof(commandClass), commandClass, null);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var fence = await db.FinancialAuthorityFences.AsNoTracking()
            .Where(value => !value.IsReleased)
            .OrderByDescending(value => value.AuthorityGeneration)
            .FirstOrDefaultAsync(ct);
        if (fence is null) return;

        var acceptance = commandClass == FinancialCommandClasses.ManualCommand
            ? fence.ManualCommandAcceptance
            : fence.NewEntryAcceptance;
        if (acceptance != AuthorityCommandAcceptanceStates.Open)
            throw new InvalidOperationException(
                $"edge-financial-command-fenced:{fence.TransitionId}:{commandClass}");
    }
}

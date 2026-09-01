using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Data;

public sealed class LegacyFinancialWriteGuard(
    IOptions<TradingCoreTransportOptions> tradingCore) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        RejectRemoteFinancialMutation(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        RejectRemoteFinancialMutation(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void RejectRemoteFinancialMutation(DbContext? context)
    {
        if (context is null
            || !string.Equals(
                tradingCore.Value.Mode,
                "Remote",
                StringComparison.Ordinal))
            return;

        var mutation = context.ChangeTracker.Entries()
            .FirstOrDefault(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && entry.Entity is TradeRecommendation
                    or Position
                    or PositionScalingExecution
                    or TradeRecord);
        if (mutation is not null)
            throw new InvalidOperationException(
                $"remote-edge-legacy-financial-write-blocked:{mutation.Metadata.ClrType.Name}");
    }
}

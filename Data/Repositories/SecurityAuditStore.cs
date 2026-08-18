using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Authentication;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public sealed class SecurityAuditStore(IDbContextFactory<AppDbContext> dbFactory)
    : ISecurityAuditStore
{
    public async Task AppendAsync(
        SecurityAuditEntry entry,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = entry.UserId,
            Action = entry.Action,
            Details = entry.Details,
            IpAddress = entry.IpAddress,
            Timestamp = entry.Timestamp
        });
        await db.SaveChangesAsync(ct);
    }
}

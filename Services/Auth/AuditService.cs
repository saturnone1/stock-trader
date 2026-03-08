using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Models;

namespace StockTrader.Services.Auth;

/// <summary>
/// Writes audit log entries to the database.
/// Uses IDbContextFactory so this Singleton-scoped service creates its own short-lived contexts.
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        IDbContextFactory<AppDbContext> dbFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditService> logger)
    {
        _dbFactory           = dbFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger              = logger;
    }

    public async Task LogAsync(int? userId, string action, string details,
        string? ipAddress = null, CancellationToken ct = default)
    {
        try
        {
            var ip = ipAddress
                ?? _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
                ?? "unknown";

            var entry = new AuditLog
            {
                UserId    = userId,
                Action    = action,
                Details   = details,
                IpAddress = ip,
                Timestamp = DateTime.UtcNow,
            };

            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.AuditLogs.Add(entry);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "AUDIT [{Action}] UserId={UserId} IP={Ip} | {Details}",
                action, userId?.ToString() ?? "anon", ip, details);
        }
        catch (Exception ex)
        {
            // Audit failures must never crash the calling flow
            _logger.LogError(ex, "Failed to write audit log entry: {Action} {Details}", action, details);
        }
    }
}

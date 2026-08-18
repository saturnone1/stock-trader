using StockTrader.Application.Authentication;

namespace StockTrader.Services.Auth;

/// <summary>
/// Captures HTTP request context and delegates persistence to the audit store.
/// Failures remain best effort so auditing cannot change the calling operation.
/// </summary>
public sealed class AuditService : ISecurityAuditSink
{
    private readonly ISecurityAuditStore _store;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        ISecurityAuditStore store,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        ILogger<AuditService> logger)
    {
        _store               = store;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider        = timeProvider;
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

            await _store.AppendAsync(
                new(
                    userId,
                    action,
                    details,
                    ip,
                    _timeProvider.GetUtcNow().UtcDateTime),
                ct);

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

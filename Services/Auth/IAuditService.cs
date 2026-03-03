namespace StockTrader.Services.Auth;

/// <summary>
/// Records security-relevant actions to the AuditLogs table.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Persists an audit entry.
    /// </summary>
    /// <param name="userId">Authenticated user ID. Null for unauthenticated actions.</param>
    /// <param name="action">Action identifier (e.g., "LOGIN_SUCCESS", "SETTINGS_CHANGED").</param>
    /// <param name="details">Human-readable detail string.</param>
    /// <param name="ipAddress">Client IP. Pass null to use the last known IP from HttpContext.</param>
    /// <param name="ct">Cancellation token.</param>
    Task LogAsync(int? userId, string action, string details,
        string? ipAddress = null, CancellationToken ct = default);
}

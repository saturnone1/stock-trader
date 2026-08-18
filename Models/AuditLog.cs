namespace StockTrader.Models;

/// <summary>
/// Immutable audit log entry for security-relevant actions.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>Null for unauthenticated actions (e.g., failed login attempts).</summary>
    public int? UserId { get; set; }

    /// <summary>Action identifier such as "LOGIN_SUCCESS", "LOGIN_FAILED", "PASSWORD_CHANGED", "SETTINGS_CHANGED".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Human-readable detail (username, changed fields, etc.).</summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>Client IP address or "unknown".</summary>
    public string IpAddress { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }
}

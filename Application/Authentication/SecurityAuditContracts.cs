namespace StockTrader.Application.Authentication;

public sealed record SecurityAuditEntry(
    int? UserId,
    string Action,
    string Details,
    string IpAddress,
    DateTime Timestamp);

public interface ISecurityAuditStore
{
    Task AppendAsync(SecurityAuditEntry entry, CancellationToken ct = default);
}

/// <summary>
/// Best-effort security audit sink. Implementations must not let persistence
/// failures change the outcome of the operation being audited.
/// </summary>
public interface ISecurityAuditSink
{
    Task LogAsync(
        int? userId,
        string action,
        string details,
        string? ipAddress = null,
        CancellationToken ct = default);
}

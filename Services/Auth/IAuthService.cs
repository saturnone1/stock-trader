using System.Security.Claims;

namespace StockTrader.Services.Auth;

public record LoginResult(bool Success, string? ErrorMessage, ClaimsPrincipal? Principal);
public record RegisterResult(bool Success, string? ErrorMessage, int UserId = 0);

/// <summary>
/// Cookie-based authentication service.
/// Uses PBKDF2-SHA256 (100,000 iterations) with random per-user salt.
/// Brute-force protection: 5 failures → 15-minute lockout.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Validates credentials and returns a ClaimsPrincipal on success.
    /// Increments failed attempt counter and enforces lockout policy.
    /// </summary>
    Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct = default);

    /// <summary>
    /// Registers a new user. Succeeds only when AllowRegistration is true
    /// or when no users exist yet (first-time setup).
    /// </summary>
    Task<RegisterResult> RegisterAsync(string username, string password, CancellationToken ct = default);

    /// <summary>Changes the password after validating the old password.</summary>
    Task<(bool Success, string? ErrorMessage)> ChangePasswordAsync(
        int userId, string oldPassword, string newPassword, CancellationToken ct = default);

    /// <summary>Returns true if at least one user account exists in the database.</summary>
    Task<bool> HasAnyUserAsync(CancellationToken ct = default);
}

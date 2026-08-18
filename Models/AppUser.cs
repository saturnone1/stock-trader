namespace StockTrader.Models;

/// <summary>
/// Application user for cookie-based authentication.
/// Passwords are stored as PBKDF2-SHA256 hashes with a random salt.
/// </summary>
public class AppUser
{
    public int Id { get; set; }

    /// <summary>Unique username (case-insensitive login).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>PBKDF2-SHA256 hash (Base64-encoded).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Random 32-byte salt (Base64-encoded).</summary>
    public string Salt { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Consecutive failed login attempts since last successful login.</summary>
    public int FailedLoginAttempts { get; set; } = 0;

    /// <summary>Account is locked until this time (UTC). Null = not locked.</summary>
    public DateTime? LockedUntil { get; set; }
}

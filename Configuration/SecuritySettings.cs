namespace StockTrader.Configuration;

public class SecuritySettings
{
    /// <summary>Sliding cookie expiration in minutes.</summary>
    public int SessionTimeoutMinutes { get; set; }

    /// <summary>Maximum consecutive failed login attempts before lockout.</summary>
    public int MaxFailedLoginAttempts { get; set; }

    /// <summary>Lockout duration in minutes after too many failed attempts.</summary>
    public int LockoutMinutes { get; set; }

    /// <summary>When true, HTTPS is required (set false for local HTTP deployments).</summary>
    public bool RequireHttps { get; set; }

    /// <summary>
    /// Allow new user registration. Should be true on first run, then set to false
    /// after the admin account is created to prevent unauthorized access.
    /// </summary>
    public bool AllowRegistration { get; set; }

    /// <summary>
    /// AES-256-GCM master key for encrypting sensitive fields (Base64, 32 bytes).
    /// Prefer the STOCKTRADER_MASTER_KEY environment variable over this field.
    /// </summary>
    public string MasterEncryptionKey { get; set; } = string.Empty;
}

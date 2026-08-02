using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Models;

namespace StockTrader.Services.Auth;

public sealed class AuthService : IAuthService
{
    // PBKDF2 parameters — NIST SP 800-132 recommendation
    private const int SaltSizeBytes  = 32;
    private const int HashSizeBytes  = 32;
    private const int Pbkdf2Iterations = 100_000;
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAuditService _audit;
    private readonly SecuritySettings _settings;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IDbContextFactory<AppDbContext> dbFactory,
        IAuditService audit,
        IOptions<SecuritySettings> opts,
        ILogger<AuthService> logger)
    {
        _dbFactory = dbFactory;
        _audit     = audit;
        _settings  = opts.Value;
        _logger    = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<LoginResult> LoginAsync(string username, string password,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.AppUsers
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower(), ct);

        if (user == null)
        {
            await _audit.LogAsync(null, "LOGIN_FAILED", $"Unknown username: {username}", ct: ct);
            return new LoginResult(false, "사용자 이름 또는 비밀번호가 올바르지 않습니다.", null);
        }

        if (!user.IsActive)
        {
            await _audit.LogAsync(user.Id, "LOGIN_FAILED", "Account inactive", ct: ct);
            // Use the same message as "user not found" to prevent user enumeration
            return new LoginResult(false, "아이디 또는 비밀번호가 올바르지 않습니다.", null);
        }

        // Lockout check
        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            var remaining = (int)(user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes + 1;
            await _audit.LogAsync(user.Id, "LOGIN_FAILED",
                $"Account locked until {user.LockedUntil:u}", ct: ct);
            return new LoginResult(false, $"계정이 잠겨 있습니다. {remaining}분 후에 다시 시도하세요.", null);
        }

        if (!VerifyPassword(password, user.PasswordHash, user.Salt))
        {
            user.FailedLoginAttempts++;

            if (user.FailedLoginAttempts >= _settings.MaxFailedLoginAttempts)
            {
                user.LockedUntil = DateTime.UtcNow.AddMinutes(_settings.LockoutMinutes);
                user.FailedLoginAttempts = 0;
                _logger.LogWarning("User {Username} locked out until {Until}", username, user.LockedUntil);
                await _audit.LogAsync(user.Id, "ACCOUNT_LOCKED",
                    $"Locked for {_settings.LockoutMinutes} min after repeated failures", ct: ct);
            }
            else
            {
                await _audit.LogAsync(user.Id, "LOGIN_FAILED",
                    $"Bad password (attempt {user.FailedLoginAttempts})", ct: ct);
            }

            await db.SaveChangesAsync(ct);
            return new LoginResult(false, "사용자 이름 또는 비밀번호가 올바르지 않습니다.", null);
        }

        // Success: reset counters
        user.FailedLoginAttempts = 0;
        user.LockedUntil         = null;
        user.LastLoginAt         = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync(user.Id, "LOGIN_SUCCESS", $"User {username} logged in", ct: ct);

        var principal = BuildPrincipal(user);
        return new LoginResult(true, null, principal);
    }

    public async Task<RegisterResult> RegisterAsync(string username, string password,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        bool hasUsers = await db.AppUsers.AnyAsync(ct);

        // Registration is allowed only when explicitly enabled or on first-time setup
        if (hasUsers && !_settings.AllowRegistration)
            return new RegisterResult(false, "등록이 비활성화되어 있습니다.");

        if (string.IsNullOrWhiteSpace(username) || username.Length < 3 || username.Length > 64)
            return new RegisterResult(false, "사용자 이름은 3~64자여야 합니다.");

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return new RegisterResult(false, "비밀번호는 최소 8자 이상이어야 합니다.");

        bool exists = await db.AppUsers
            .AnyAsync(u => u.Username.ToLower() == username.ToLower(), ct);
        if (exists)
            return new RegisterResult(false, "이미 사용 중인 사용자 이름입니다.");

        var (hash, salt) = HashPassword(password);
        var user = new AppUser
        {
            Username     = username,
            PasswordHash = hash,
            Salt         = salt,
            IsActive     = true,
        };

        db.AppUsers.Add(user);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync(user.Id, "USER_REGISTERED", $"New user: {username}", ct: ct);
        _logger.LogInformation("New user registered: {Username} (Id={Id})", username, user.Id);

        return new RegisterResult(true, null, user.Id);
    }

    public async Task<(bool Success, string? ErrorMessage)> ChangePasswordAsync(
        int userId, string oldPassword, string newPassword, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.AppUsers.FindAsync([userId], ct);
        if (user == null)
            return (false, "사용자를 찾을 수 없습니다.");

        // Validate new password format BEFORE verifying old password to avoid
        // revealing whether the old password is correct when the new one is invalid.
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return (false, "새 비밀번호는 최소 8자 이상이어야 합니다.");

        if (!VerifyPassword(oldPassword, user.PasswordHash, user.Salt))
        {
            await _audit.LogAsync(userId, "PASSWORD_CHANGE_FAILED", "Wrong current password", ct: ct);
            return (false, "현재 비밀번호가 올바르지 않습니다.");
        }

        var (hash, salt) = HashPassword(newPassword);
        user.PasswordHash = hash;
        user.Salt         = salt;
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync(userId, "PASSWORD_CHANGED", $"User {user.Username} changed password", ct: ct);
        return (true, null);
    }

    public async Task<bool> HasAnyUserAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.AppUsers.AnyAsync(ct);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static (string Hash, string Salt) HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            saltBytes,
            Pbkdf2Iterations,
            HashAlgorithm,
            HashSizeBytes);

        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    private static bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        var saltBytes    = Convert.FromBase64String(storedSalt);
        var expectedHash = Convert.FromBase64String(storedHash);

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            saltBytes,
            Pbkdf2Iterations,
            HashAlgorithm,
            HashSizeBytes);

        // Constant-time comparison to prevent timing attacks
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static ClaimsPrincipal BuildPrincipal(AppUser user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
        };

        var identity  = new ClaimsIdentity(claims, "CookieAuth");
        return new ClaimsPrincipal(identity);
    }
}

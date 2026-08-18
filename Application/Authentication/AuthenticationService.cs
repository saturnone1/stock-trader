using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace StockTrader.Application.Authentication;

public sealed class AuthenticationService(
    IAuthenticationUserStore users,
    ISecurityAuditSink audit,
    AuthenticationPolicy policy,
    TimeProvider timeProvider,
    ILogger<AuthenticationService> logger) : IUserAuthenticationService
{
    // Security protocol invariants. Changing these requires a password-format migration plan.
    private const int SaltSizeBytes = 32;
    private const int HashSizeBytes = 32;
    private const int Pbkdf2Iterations = 100_000;
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    private const string InvalidCredentialsMessage =
        "사용자 이름 또는 비밀번호가 올바르지 않습니다.";

    public async Task<LoginResult> LoginAsync(
        string username,
        string password,
        CancellationToken ct = default)
    {
        var user = await users.FindByUsernameAsync(username, ct);
        if (user is null)
        {
            await audit.LogAsync(
                null,
                "LOGIN_FAILED",
                $"Unknown username: {username}",
                ct: ct);
            return new(false, InvalidCredentialsMessage, null);
        }

        if (!user.IsActive)
        {
            await audit.LogAsync(user.Id, "LOGIN_FAILED", "Account inactive", ct: ct);
            return new(false, InvalidCredentialsMessage, null);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (user.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            var remaining = Math.Max(1, (int)Math.Ceiling((lockedUntil - now).TotalMinutes));
            await audit.LogAsync(
                user.Id,
                "LOGIN_FAILED",
                $"Account locked until {lockedUntil:u}",
                ct: ct);
            return new(
                false,
                $"계정이 잠겨 있습니다. {remaining}분 후에 다시 시도하세요.",
                null);
        }

        if (!VerifyPassword(password, user.PasswordHash, user.Salt))
        {
            var failure = await users.RecordFailedLoginAsync(
                user.Id,
                now,
                policy.MaximumFailedLoginAttempts,
                now.Add(policy.LockoutDuration),
                ct);
            if (failure.NewlyLocked)
            {
                logger.LogWarning(
                    "User {Username} locked out until {Until}",
                    username,
                    failure.LockedUntil);
                await audit.LogAsync(
                    user.Id,
                    "ACCOUNT_LOCKED",
                    $"Locked for {policy.LockoutDuration.TotalMinutes:F0} min after repeated failures",
                    ct: ct);
            }
            else
            {
                await audit.LogAsync(
                    user.Id,
                    "LOGIN_FAILED",
                    failure.LockedUntil is { } concurrentLock && concurrentLock > now
                        ? $"Account locked until {concurrentLock:u}"
                        : $"Bad password (attempt {failure.FailedLoginAttempts})",
                    ct: ct);
            }
            return new(false, InvalidCredentialsMessage, null);
        }

        var success = await users.RecordSuccessfulLoginAsync(user.Id, now, ct);
        if (!success.Accepted && success.LockedUntil is { } concurrentLockedUntil)
            return await LockedResultAsync(user.Id, concurrentLockedUntil, now, ct);

        await audit.LogAsync(
            user.Id,
            "LOGIN_SUCCESS",
            $"User {username} logged in",
            ct: ct);

        return new(true, null, BuildPrincipal(user));
    }

    public async Task<RegisterResult> RegisterAsync(
        string username,
        string password,
        CancellationToken ct = default)
    {
        var hasUsers = await users.HasAnyAsync(ct);
        if (hasUsers && !policy.AllowRegistration)
            return new(false, "등록이 비활성화되어 있습니다.");

        if (string.IsNullOrWhiteSpace(username)
            || username.Length is < AuthenticationPolicy.MinimumUsernameLength
                or > AuthenticationPolicy.MaximumUsernameLength)
        {
            return new(false, "사용자 이름은 3~64자여야 합니다.");
        }

        if (string.IsNullOrWhiteSpace(password)
            || password.Length < AuthenticationPolicy.MinimumPasswordLength)
        {
            return new(false, "비밀번호는 최소 8자 이상이어야 합니다.");
        }

        if (await users.FindByUsernameAsync(username, ct) is not null)
            return new(false, "이미 사용 중인 사용자 이름입니다.");

        var (hash, salt) = HashPassword(password);
        var creation = await users.TryCreateAsync(
            new(
                username,
                hash,
                salt,
                timeProvider.GetUtcNow().UtcDateTime),
            ct);
        if (creation.Status == AuthenticationUserCreationStatus.UsernameConflict)
            return new(false, "이미 사용 중인 사용자 이름입니다.");

        await audit.LogAsync(
            creation.UserId,
            "USER_REGISTERED",
            $"New user: {username}",
            ct: ct);
        logger.LogInformation(
            "New user registered: {Username} (Id={Id})",
            username,
            creation.UserId);
        return new(true, null, creation.UserId);
    }

    public async Task<(bool Success, string? ErrorMessage)> ChangePasswordAsync(
        int userId,
        string oldPassword,
        string newPassword,
        CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null)
            return (false, "사용자를 찾을 수 없습니다.");

        if (string.IsNullOrWhiteSpace(newPassword)
            || newPassword.Length < AuthenticationPolicy.MinimumPasswordLength)
        {
            return (false, "새 비밀번호는 최소 8자 이상이어야 합니다.");
        }

        if (!VerifyPassword(oldPassword, user.PasswordHash, user.Salt))
        {
            await audit.LogAsync(
                userId,
                "PASSWORD_CHANGE_FAILED",
                "Wrong current password",
                ct: ct);
            return (false, "현재 비밀번호가 올바르지 않습니다.");
        }

        var (hash, salt) = HashPassword(newPassword);
        await users.SavePasswordAsync(userId, hash, salt, ct);
        await audit.LogAsync(
            userId,
            "PASSWORD_CHANGED",
            $"User {user.Username} changed password",
            ct: ct);
        return (true, null);
    }

    public Task<bool> HasAnyUserAsync(CancellationToken ct = default) =>
        users.HasAnyAsync(ct);

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

    private static bool VerifyPassword(
        string password,
        string storedHash,
        string storedSalt)
    {
        try
        {
            var saltBytes = Convert.FromBase64String(storedSalt);
            var expectedHash = Convert.FromBase64String(storedHash);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                saltBytes,
                Pbkdf2Iterations,
                HashAlgorithm,
                HashSizeBytes);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            // A malformed persisted credential must fail closed, not crash the login endpoint.
            return false;
        }
    }

    private static ClaimsPrincipal BuildPrincipal(AuthenticationUser user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "CookieAuth"));
    }

    private async Task<LoginResult> LockedResultAsync(
        int userId,
        DateTime lockedUntil,
        DateTime observedAt,
        CancellationToken ct)
    {
        var remaining = Math.Max(
            1,
            (int)Math.Ceiling((lockedUntil - observedAt).TotalMinutes));
        await audit.LogAsync(
            userId,
            "LOGIN_FAILED",
            $"Account locked until {lockedUntil:u}",
            ct: ct);
        return new(
            false,
            $"계정이 잠겨 있습니다. {remaining}분 후에 다시 시도하세요.",
            null);
    }
}

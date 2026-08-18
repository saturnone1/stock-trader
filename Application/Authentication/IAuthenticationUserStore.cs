namespace StockTrader.Application.Authentication;

/// <summary>
/// Purpose-specific persistence boundary for authentication state.
/// Implementations own entity mapping and database concurrency details.
/// </summary>
public interface IAuthenticationUserStore
{
    Task<bool> HasAnyAsync(CancellationToken ct = default);

    Task<AuthenticationUser?> FindByUsernameAsync(
        string username,
        CancellationToken ct = default);

    Task<AuthenticationUser?> FindByIdAsync(
        int userId,
        CancellationToken ct = default);

    Task<AuthenticationUserCreation> TryCreateAsync(
        NewAuthenticationUser user,
        CancellationToken ct = default);

    Task<AuthenticationLoginFailure> RecordFailedLoginAsync(
        int userId,
        DateTime observedAt,
        int maximumFailedLoginAttempts,
        DateTime lockoutUntil,
        CancellationToken ct = default);

    Task<AuthenticationLoginSuccess> RecordSuccessfulLoginAsync(
        int userId,
        DateTime observedAt,
        CancellationToken ct = default);

    Task SavePasswordAsync(
        int userId,
        string passwordHash,
        string salt,
        CancellationToken ct = default);
}

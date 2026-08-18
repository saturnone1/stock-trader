namespace StockTrader.Application.Authentication;

public sealed record AuthenticationUser(
    int Id,
    string Username,
    string PasswordHash,
    string Salt,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    bool IsActive,
    int FailedLoginAttempts,
    DateTime? LockedUntil);

public sealed record NewAuthenticationUser(
    string Username,
    string PasswordHash,
    string Salt,
    DateTime CreatedAt);

public enum AuthenticationUserCreationStatus
{
    Created,
    UsernameConflict
}

public sealed record AuthenticationUserCreation(
    AuthenticationUserCreationStatus Status,
    int UserId = 0);

public sealed record AuthenticationLoginFailure(
    int FailedLoginAttempts,
    DateTime? LockedUntil,
    bool NewlyLocked);

public sealed record AuthenticationLoginSuccess(
    bool Accepted,
    DateTime? LockedUntil);

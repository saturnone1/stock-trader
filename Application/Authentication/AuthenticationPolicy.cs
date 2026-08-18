namespace StockTrader.Application.Authentication;

public sealed class AuthenticationPolicy
{
    public const int MinimumUsernameLength = 3;
    public const int MaximumUsernameLength = 64;
    public const int MinimumPasswordLength = 8;

    public AuthenticationPolicy(
        int maximumFailedLoginAttempts,
        TimeSpan lockoutDuration,
        bool allowRegistration)
    {
        if (maximumFailedLoginAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumFailedLoginAttempts));
        if (lockoutDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lockoutDuration));

        MaximumFailedLoginAttempts = maximumFailedLoginAttempts;
        LockoutDuration = lockoutDuration;
        AllowRegistration = allowRegistration;
    }

    public int MaximumFailedLoginAttempts { get; }
    public TimeSpan LockoutDuration { get; }
    public bool AllowRegistration { get; }
}

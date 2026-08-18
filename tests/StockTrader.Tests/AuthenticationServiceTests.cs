using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StockTrader.Application.Authentication;

namespace StockTrader.Tests;

public sealed class AuthenticationServiceTests
{
    private static readonly DateTimeOffset Observation =
        new(2026, 8, 19, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    public async Task FailedCredentialsLockAtConfiguredThresholdUsingInjectedClock()
    {
        var fixture = new Fixture(maximumFailures: 2);
        var registration = await fixture.Service.RegisterAsync("trader", "password-1");

        (await fixture.Service.LoginAsync("trader", "wrong-1")).Success.Should().BeFalse();
        fixture.Store.User!.FailedLoginAttempts.Should().Be(1);
        fixture.Store.User.LockedUntil.Should().BeNull();

        (await fixture.Service.LoginAsync("trader", "wrong-2")).Success.Should().BeFalse();
        fixture.Store.User!.FailedLoginAttempts.Should().Be(0);
        fixture.Store.User.LockedUntil.Should().Be(Observation.UtcDateTime.AddMinutes(15));
        fixture.Audit.Entries.Should().Contain(entry =>
            entry.UserId == registration.UserId && entry.Action == "ACCOUNT_LOCKED");

        var locked = await fixture.Service.LoginAsync("trader", "password-1");
        locked.Success.Should().BeFalse();
        locked.ErrorMessage.Should().Contain("15분");
    }

    [Fact]
    public async Task LoginAtLockBoundarySucceedsAndResetsPersistedState()
    {
        var fixture = new Fixture(maximumFailures: 1);
        await fixture.Service.RegisterAsync("trader", "password-1");
        await fixture.Service.LoginAsync("trader", "wrong");
        fixture.Clock.UtcNow = Observation.AddMinutes(15);

        var result = await fixture.Service.LoginAsync("TRADER", "password-1");

        result.Success.Should().BeTrue();
        result.Principal!.FindFirstValue(ClaimTypes.Name).Should().Be("trader");
        fixture.Store.User!.FailedLoginAttempts.Should().Be(0);
        fixture.Store.User.LockedUntil.Should().BeNull();
        fixture.Store.User.LastLoginAt.Should().Be(fixture.Clock.UtcNow.UtcDateTime);
    }

    [Fact]
    public async Task MalformedPersistedCredentialFailsClosedWithoutCrashingEndpointFlow()
    {
        var fixture = new Fixture();
        fixture.Store.User = User(passwordHash: "not-base64", salt: "not-base64");

        var result = await fixture.Service.LoginAsync("trader", "anything");

        result.Success.Should().BeFalse();
        fixture.Store.User.FailedLoginAttempts.Should().Be(1);
        fixture.Audit.Entries.Should().Contain(entry => entry.Action == "LOGIN_FAILED");
    }

    [Fact]
    public async Task InactiveAndUnknownUsersReceiveTheSamePublicError()
    {
        var unknownFixture = new Fixture();
        var unknown = await unknownFixture.Service.LoginAsync("missing", "password-1");
        var inactiveFixture = new Fixture();
        inactiveFixture.Store.User = User() with { IsActive = false };

        var inactive = await inactiveFixture.Service.LoginAsync("trader", "password-1");

        inactive.Success.Should().BeFalse();
        inactive.ErrorMessage.Should().Be(unknown.ErrorMessage);
    }

    [Fact]
    public async Task ExistingInstallationRejectsRegistrationWhenDisabled()
    {
        var fixture = new Fixture(allowRegistration: false);
        fixture.Store.User = User();

        var result = await fixture.Service.RegisterAsync("another", "password-1");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("등록이 비활성화되어 있습니다.");
        fixture.Store.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task FirstInstallationCanRegisterEvenWhenOngoingRegistrationIsDisabled()
    {
        var fixture = new Fixture(allowRegistration: false);

        var result = await fixture.Service.RegisterAsync("first-user", "password-1");

        result.Success.Should().BeTrue();
        fixture.Store.User!.CreatedAt.Should().Be(Observation.UtcDateTime);
        fixture.Audit.Entries.Should().ContainSingle(entry =>
            entry.Action == "USER_REGISTERED" && entry.UserId == result.UserId);
    }

    [Fact]
    public async Task PasswordChangeReplacesCredentialAndPreservesLoginCompatibility()
    {
        var fixture = new Fixture();
        await fixture.Service.RegisterAsync("trader", "password-1");

        var changed = await fixture.Service.ChangePasswordAsync(
            fixture.Store.User!.Id,
            "password-1",
            "password-2");

        changed.Success.Should().BeTrue();
        (await fixture.Service.LoginAsync("trader", "password-1")).Success.Should().BeFalse();
        (await fixture.Service.LoginAsync("trader", "password-2")).Success.Should().BeTrue();
        fixture.Audit.Entries.Should().Contain(entry => entry.Action == "PASSWORD_CHANGED");
    }

    private static AuthenticationUser User(
        string passwordHash = "invalid",
        string salt = "invalid") => new(
            7,
            "trader",
            passwordHash,
            salt,
            Observation.UtcDateTime.AddDays(-1),
            null,
            true,
            0,
            null);

    private sealed class Fixture
    {
        public InMemoryAuthenticationUserStore Store { get; } = new();
        public RecordingAuditSink Audit { get; } = new();
        public MutableTimeProvider Clock { get; } = new(Observation);
        public AuthenticationService Service { get; }

        public Fixture(
            int maximumFailures = 5,
            bool allowRegistration = true)
        {
            Service = new(
                Store,
                Audit,
                new AuthenticationPolicy(
                    maximumFailures,
                    TimeSpan.FromMinutes(15),
                    allowRegistration),
                Clock,
                NullLogger<AuthenticationService>.Instance);
        }
    }

    private sealed class InMemoryAuthenticationUserStore : IAuthenticationUserStore
    {
        public AuthenticationUser? User { get; set; }
        public int CreateCalls { get; private set; }

        public Task<bool> HasAnyAsync(CancellationToken ct = default) =>
            Task.FromResult(User is not null);

        public Task<AuthenticationUser?> FindByUsernameAsync(
            string username,
            CancellationToken ct = default) =>
            Task.FromResult(User is not null
                && string.Equals(User.Username, username, StringComparison.OrdinalIgnoreCase)
                    ? User
                    : null);

        public Task<AuthenticationUser?> FindByIdAsync(
            int userId,
            CancellationToken ct = default) =>
            Task.FromResult(User?.Id == userId ? User : null);

        public Task<AuthenticationUserCreation> TryCreateAsync(
            NewAuthenticationUser user,
            CancellationToken ct = default)
        {
            CreateCalls++;
            if (User is not null)
                return Task.FromResult(new AuthenticationUserCreation(
                    AuthenticationUserCreationStatus.UsernameConflict));
            User = new(
                7,
                user.Username,
                user.PasswordHash,
                user.Salt,
                user.CreatedAt,
                null,
                true,
                0,
                null);
            return Task.FromResult(new AuthenticationUserCreation(
                AuthenticationUserCreationStatus.Created,
                User.Id));
        }

        public Task<AuthenticationLoginFailure> RecordFailedLoginAsync(
            int userId,
            DateTime observedAt,
            int maximumFailedLoginAttempts,
            DateTime lockoutUntil,
            CancellationToken ct = default)
        {
            if (User!.LockedUntil is { } currentLock && currentLock > observedAt)
            {
                return Task.FromResult(new AuthenticationLoginFailure(
                    User.FailedLoginAttempts,
                    currentLock,
                    false));
            }

            var attempts = User.FailedLoginAttempts + 1;
            var newlyLocked = attempts >= maximumFailedLoginAttempts;
            User = User with
            {
                FailedLoginAttempts = newlyLocked ? 0 : attempts,
                LockedUntil = newlyLocked ? lockoutUntil : null
            };
            return Task.FromResult(new AuthenticationLoginFailure(
                User.FailedLoginAttempts,
                User.LockedUntil,
                newlyLocked));
        }

        public Task<AuthenticationLoginSuccess> RecordSuccessfulLoginAsync(
            int userId,
            DateTime observedAt,
            CancellationToken ct = default)
        {
            if (User!.LockedUntil is { } currentLock && currentLock > observedAt)
                return Task.FromResult(new AuthenticationLoginSuccess(false, currentLock));
            User = User with
            {
                FailedLoginAttempts = 0,
                LockedUntil = null,
                LastLoginAt = observedAt
            };
            return Task.FromResult(new AuthenticationLoginSuccess(true, null));
        }

        public Task SavePasswordAsync(
            int userId,
            string passwordHash,
            string salt,
            CancellationToken ct = default)
        {
            User = User! with { PasswordHash = passwordHash, Salt = salt };
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAuditSink : ISecurityAuditSink
    {
        public List<(int? UserId, string Action, string Details)> Entries { get; } = [];

        public Task LogAsync(
            int? userId,
            string action,
            string details,
            string? ipAddress = null,
            CancellationToken ct = default)
        {
            Entries.Add((userId, action, details));
            return Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}

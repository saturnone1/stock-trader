using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using StockTrader.Application.Authentication;
using StockTrader.Services.Auth;

namespace StockTrader.Tests;

public sealed class AuditServiceTests
{
    [Fact]
    public async Task AuditUsesRequestIpAndInjectedObservationTime()
    {
        var store = new RecordingStore();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        var observedAt = new DateTimeOffset(2026, 8, 19, 4, 0, 0, TimeSpan.Zero);
        var service = new AuditService(
            store,
            new HttpContextAccessor { HttpContext = context },
            new FixedTimeProvider(observedAt),
            NullLogger<AuditService>.Instance);

        await service.LogAsync(7, "LOGIN_SUCCESS", "ok");

        store.Entry.Should().Be(new SecurityAuditEntry(
            7,
            "LOGIN_SUCCESS",
            "ok",
            "192.0.2.10",
            observedAt.UtcDateTime));
    }

    [Fact]
    public async Task AuditPersistenceFailureNeverChangesCallingFlow()
    {
        var service = new AuditService(
            new ThrowingStore(),
            new HttpContextAccessor(),
            new FixedTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<AuditService>.Instance);

        var action = () => service.LogAsync(null, "LOGIN_FAILED", "details");

        await action.Should().NotThrowAsync();
    }

    private sealed class RecordingStore : ISecurityAuditStore
    {
        public SecurityAuditEntry? Entry { get; private set; }
        public Task AppendAsync(
            SecurityAuditEntry entry,
            CancellationToken ct = default)
        {
            Entry = entry;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingStore : ISecurityAuditStore
    {
        public Task AppendAsync(
            SecurityAuditEntry entry,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("database unavailable");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

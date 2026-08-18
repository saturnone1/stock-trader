using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Services.Notification;

namespace StockTrader.Tests;

public sealed class DiscordNotificationChannelTests
{
    [Fact]
    public async Task AlertPayloadUsesTheInjectedUtcObservation()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-19T04:05:06.0000000+00:00");
        var handler = new CaptureHandler();
        var settings = new NotificationSettings
        {
            EnableDiscord = true,
            DiscordWebhookUrl = "https://discord.example.test/webhook"
        };
        var channel = new DiscordNotificationChannel(
            new HttpClient(handler),
            new FixedSettingsProvider(settings),
            Options.Create(settings),
            NullLogger<DiscordNotificationChannel>.Instance,
            new FixedTimeProvider(observedAt));

        await channel.SendAlertAsync("clock test");

        using var payload = JsonDocument.Parse(handler.Body!);
        payload.RootElement.GetProperty("embeds")[0]
            .GetProperty("timestamp").GetString()
            .Should().Be(observedAt.ToString("o"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FixedSettingsProvider(NotificationSettings settings)
        : INotificationSettingsProvider
    {
        public Task<NotificationSettings> GetEffectiveSettingsAsync(
            CancellationToken ct = default) => Task.FromResult(settings);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}

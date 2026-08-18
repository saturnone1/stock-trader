using System.Text.Json;
using FluentAssertions;
using StockTrader.Services.Broker;
using StockTrader.Services.LsSecurities;

namespace StockTrader.Tests;

public sealed class LsOrderTimestampParserTests
{
    [Theory]
    [InlineData("20260819", "134501", "2026-08-19T04:45:01.0000000Z")]
    [InlineData("20260819", "134501123", "2026-08-19T04:45:01.1230000Z")]
    public void ParsesActualKoreanOrderTimeAsUtc(
        string date,
        string time,
        string expectedUtc)
    {
        using var document = JsonDocument.Parse(
            $$"""{"OrdDt":"{{date}}","OrdTime":"{{time}}"}""");

        var parsed = LsOrderTimestampParser.TryParseUtc(
            document.RootElement,
            new DateTime(2000, 1, 1),
            LsAuthService.KstZone,
            out var submittedAt);

        parsed.Should().BeTrue();
        submittedAt.Should().Be(DateTime.Parse(
            expectedUtc,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind));
    }

    [Fact]
    public void UsesSingleDayRequestDateWhenResponseOmitsOrderDate()
    {
        using var document = JsonDocument.Parse("""{"OrdTime": "090000"}""");

        var parsed = LsOrderTimestampParser.TryParseUtc(
            document.RootElement,
            new DateTime(2026, 8, 19),
            LsAuthService.KstZone,
            out var submittedAt);

        parsed.Should().BeTrue();
        submittedAt.Should().Be(
            new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("20260819", "")]
    [InlineData("20260819", "256100")]
    [InlineData("invalid", "090000")]
    public void RejectsMissingOrInvalidTimestampEvidence(string date, string time)
    {
        using var document = JsonDocument.Parse(
            $$"""{"OrdDt":"{{date}}","OrdTime":"{{time}}"}""");

        LsOrderTimestampParser.TryParseUtc(
                document.RootElement,
                new DateTime(2026, 8, 19),
                LsAuthService.KstZone,
                out _)
            .Should().BeFalse();
    }
}

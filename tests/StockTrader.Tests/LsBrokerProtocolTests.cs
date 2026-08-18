using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockTrader.Application.Accounts;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Broker;
using StockTrader.Services.LsSecurities;

namespace StockTrader.Tests;

public sealed class LsBrokerProtocolTests
{
    private static readonly LsSecuritiesSettings Settings = new()
    {
        AccountNo = "00000000",
        AccountPassword = "0000",
        AppKey = "app-key-test",
        AppSecret = "app-secret-test",
        IsPaper = true,
        PaperBaseUrl = "https://paper.example.test",
        BaseUrl = "https://live.example.test"
    };

    [Fact]
    public void CurrentOrderAndCancelTransactionsOwnTheirMatchingBlockNames()
    {
        var entry = Json(LsBrokerProtocol.CreateEntryOrderBody(
            Settings,
            new TradeRecommendation
            {
                Symbol = "005930",
                ShareQuantity = 3,
                EntryPrice = 72_500m
            }));
        var cancel = Json(LsBrokerProtocol.CreateCancelOrderBody(Settings, 12345));

        LsBrokerProtocol.OrderTransactionCode.Should().Be("CSPAT00601");
        LsBrokerProtocol.CancelTransactionCode.Should().Be("CSPAT00801");
        var entryBlock = entry.RootElement.GetProperty("CSPAT00601InBlock1");
        entryBlock.GetProperty("IsuNo").GetString().Should().Be("A005930");
        entryBlock.GetProperty("OrdQty").GetInt32().Should().Be(3);
        entryBlock.GetProperty("OrdPrc").GetDecimal().Should().Be(72_500m);
        entryBlock.GetProperty("BnsTpCode").GetString().Should().Be("2");
        cancel.RootElement.GetProperty("CSPAT00801InBlock1")
            .GetProperty("OrgOrdNo").GetInt64().Should().Be(12345);
    }

    [Fact]
    public void PositionRequestUsesThePublishedT0424Fields()
    {
        var body = Json(LsBrokerProtocol.CreatePositionsBody());
        var block = body.RootElement.GetProperty("t0424InBlock");

        block.GetProperty("prcgb").GetString().Should().Be("1");
        block.GetProperty("chegb").GetString().Should().Be("2");
        block.GetProperty("dangb").GetString().Should().Be("0");
        block.GetProperty("charge").GetString().Should().Be("0");
        block.GetProperty("cts_expcode").GetString().Should().BeEmpty();
        block.TryGetProperty("pession", out _).Should().BeFalse();
        block.TryGetProperty("cts_medession", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("CSPAT00601OutBlock2")]
    [InlineData("CSPAT00600OutBlock2")]
    public void AcceptedOrderEvidenceReadsCurrentAndLegacyResponseBlocks(string blockName)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [blockName] = new Dictionary<string, object> { ["OrdNo"] = "778899" }
        });

        LsBrokerResponseParser.TryReadOrderId(json, out var orderId).Should().BeTrue();
        orderId.Should().Be("778899");
    }

    [Fact]
    public void PositionAndAccountParsersAcceptPublishedNumericAndTextNumberShapes()
    {
        const string positionsJson = """
            {
              "t0424OutBlock1": [
                { "expcode": "A005930", "janqty": "10", "pamt": "70123.45", "price": 71000 },
                { "expcode": "000000", "janqty": 0, "pamt": 1, "price": 1 }
              ]
            }
            """;
        const string accountJson = """
            {
              "CSPAQ12300OutBlock2": {
                "DpsastTotamt": "1000000.50",
                "D2Dps": 250000,
                "MnyOrdAbleAmt": "200000",
                "InvstOrgAmt": -1500.25
              }
            }
            """;
        var fetchedAt = new DateTime(2026, 8, 19, 1, 2, 3, DateTimeKind.Utc);

        LsBrokerResponseParser.ParsePositions(positionsJson).Should().Equal(
            new BrokerPositionSnapshot("005930", 10, 70_123.45m, 71_000m));
        LsBrokerResponseParser.TryParseAccount(
            accountJson, "00000000", fetchedAt, out var account).Should().BeTrue();
        account.Should().NotBeNull();
        account!.TotalEquity.Should().Be(1_000_000.50m);
        account.Cash.Should().Be(250_000m);
        account.BuyingPower.Should().Be(200_000m);
        account.UnrealizedPnL.Should().Be(-1_500.25m);
        account.FetchedAt.Should().Be(fetchedAt);
    }

    [Fact]
    public void UtcWindowQueriesEveryOverlappingKoreanTradingDate()
    {
        var dates = LsOrderHistoryWindow.KoreanTradingDates(
            Utc(2026, 8, 18, 15, 30, 0),
            Utc(2026, 8, 19, 15, 30, 0),
            LsAuthService.KstZone);

        dates.Should().Equal(new DateOnly(2026, 8, 19), new DateOnly(2026, 8, 20));
        LsOrderHistoryWindow.KoreanTradingDates(
                Utc(2026, 8, 19, 1, 0, 0),
                Utc(2026, 8, 19, 0, 59, 59),
                LsAuthService.KstZone)
            .Should().BeEmpty();
    }

    [Fact]
    public void HistoryParserFiltersExactUtcWindowAndRejectsUnusableEvidence()
    {
        const string json = """
            {
              "CSPAQ13700OutBlock3": [
                { "OrdNo": 1, "IsuNo": "A005930", "BnsTpCode": "2", "OrdQty": 10, "ExecQty": 10, "OrdPrc": 70000, "ExecPrc": 70100, "OrdTime": "002959" },
                { "OrdNo": "2", "IsuNo": "A005930", "BnsTpCode": "2", "OrdQty": "10", "ExecQty": "4", "OrdPrc": "70000", "ExecPrc": "70100.5", "OrdTime": "003000" },
                { "OrdNo": 3, "IsuNo": "A005930", "BnsTpCode": "1", "OrdQty": 0, "ExecQty": 0, "OrdTime": "004000" },
                { "OrdNo": 4, "IsuNo": "A005930", "BnsTpCode": "1", "OrdQty": 1, "ExecQty": 0, "OrdTime": "invalid" }
              ]
            }
            """;

        var parsed = LsBrokerResponseParser.ParseOrderHistory(
            json,
            new DateOnly(2026, 8, 19),
            LsAuthService.KstZone,
            Utc(2026, 8, 18, 15, 30, 0),
            Utc(2026, 8, 18, 16, 0, 0));

        parsed.Orders.Should().ContainSingle();
        parsed.InvalidTimestampCount.Should().Be(1);
        parsed.InvalidQuantityCount.Should().Be(1);
        var order = parsed.Orders[0];
        order.OrderId.Should().Be("2");
        order.Symbol.Should().Be("005930");
        order.Direction.Should().Be(TradeDirection.Long);
        order.Quantity.Should().Be(10);
        order.FilledQuantity.Should().Be(4);
        order.Status.Should().Be(BrokerOrderStatus.PartiallyFilled);
        order.OrderPrice.Should().Be(70_000m);
        order.AverageFillPrice.Should().Be(70_100.5m);
        order.SubmittedAt.Should().Be(Utc(2026, 8, 18, 15, 30, 0));
    }

    [Fact]
    public async Task FacadeSubmitsEntryWithCurrentTransactionAndPreservesAcceptanceEvidence()
    {
        var requestedAt = new DateTimeOffset(Utc(2026, 8, 19, 1, 2, 3));
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/oauth2/token")
            {
                return JsonResponse("""{"access_token":"token-test"}""");
            }
            return JsonResponse("""{"CSPAT00601OutBlock2":{"OrdNo":778899}}""");
        });
        var http = new HttpClient(handler);
        var auth = new LsAuthService(
            Options.Create(Settings),
            new FixedTimeProvider(requestedAt),
            NullLogger<LsAuthService>.Instance);
        var sut = new LsSecuritiesBrokerService(
            http,
            auth,
            new FixedTimeProvider(requestedAt),
            NullLogger<LsSecuritiesBrokerService>.Instance);

        var order = await sut.SubmitEntryOrderAsync(new TradeRecommendation
        {
            Symbol = "005930",
            ShareQuantity = 2,
            EntryPrice = 72_000m
        });

        order.Should().NotBeNull();
        order!.OrderId.Should().Be("778899");
        order.SubmittedAt.Should().Be(requestedAt.UtcDateTime);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[^1].TransactionCode.Should().Be("CSPAT00601");
        handler.Requests[^1].Body.Should().Contain("CSPAT00601InBlock1");
    }

    private static JsonDocument Json(object value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value));

    private static DateTime Utc(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed record RecordedRequest(string? TransactionCode, string Body);

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Headers.TryGetValues("tr_cd", out var values)
                    ? values.Single()
                    : null,
                body));
            return responseFactory(request);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}

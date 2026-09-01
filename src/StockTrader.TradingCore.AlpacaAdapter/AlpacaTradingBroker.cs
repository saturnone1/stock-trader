using Alpaca.Markets;

namespace StockTrader.TradingCore.Broker;

public sealed class AlpacaTradingBroker : ITradingBroker, IDisposable
{
    private readonly IAlpacaTradingClient _client;
    private readonly TimeProvider _clock;

    public AlpacaTradingBroker(
        string apiKey,
        string apiSecret,
        bool isPaper,
        TimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            throw new ArgumentException("Alpaca credentials are required.");
        _clock = clock;
        var key = new SecretKey(apiKey, apiSecret);
        _client = isPaper
            ? Alpaca.Markets.Environments.Paper.GetAlpacaTradingClient(key)
            : Alpaca.Markets.Environments.Live.GetAlpacaTradingClient(key);
    }

    public async Task<BrokerOrderEvidence> SubmitEntryAsync(
        BrokerEntryOrderRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ClientOrderId)
            || string.IsNullOrWhiteSpace(request.Symbol) || request.Quantity <= 0
            || request.TakeProfitPrice <= 0 || request.StopLossPrice <= 0)
            throw new ArgumentException("Invalid protected entry request.", nameof(request));
        var order = await _client.PostOrderAsync(
            MarketOrder.Buy(request.Symbol, request.Quantity)
                .WithDuration(TimeInForce.Day)
                .WithClientOrderId(request.ClientOrderId)
                .Bracket(Math.Round(request.TakeProfitPrice, 2), Math.Round(request.StopLossPrice, 2)), ct);
        return Map(order, UtcNow);
    }

    public async Task<BrokerOrderEvidence> IncreasePositionAsync(
        BrokerPositionOrderRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ClientOrderId)
            || string.IsNullOrWhiteSpace(request.Symbol) || request.Quantity <= 0)
            throw new ArgumentException("Invalid scale-in request.");
        var order = await _client.PostOrderAsync(
            MarketOrder.Buy(request.Symbol, request.Quantity)
                .WithDuration(TimeInForce.Day)
                .WithClientOrderId(request.ClientOrderId), ct);
        return Map(order, UtcNow);
    }

    public async Task<BrokerOrderEvidence> ClosePositionAsync(
        BrokerPositionOrderRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ClientOrderId)
            || string.IsNullOrWhiteSpace(request.Symbol) || request.Quantity <= 0)
            throw new ArgumentException("Invalid close request.");
        var order = await _client.PostOrderAsync(
            MarketOrder.Sell(request.Symbol, request.Quantity)
                .WithDuration(TimeInForce.Day)
                .WithClientOrderId(request.ClientOrderId), ct);
        return Map(order, UtcNow);
    }

    public Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
        Guid.TryParse(orderId, out var id)
            ? _client.CancelOrderAsync(id, ct)
            : Task.FromResult(false);

    public async Task<IReadOnlyList<BrokerPositionEvidence>> GetPositionsAsync(
        CancellationToken ct = default) =>
        (await _client.ListPositionsAsync(ct)).Select(position => new BrokerPositionEvidence(
            position.Symbol, (int)position.IntegerQuantity, position.AverageEntryPrice,
            position.AssetCurrentPrice ?? position.AverageEntryPrice)).ToArray();

    public async Task<BrokerAccountEvidence> GetAccountAsync(CancellationToken ct = default)
    {
        var account = await _client.GetAccountAsync(ct);
        return new BrokerAccountEvidence(account.AccountId.ToString(), account.Equity ?? 0m,
            account.LastEquity,
            account.TradableCash, account.BuyingPower ?? 0m, account.IsTradingBlocked, UtcNow);
    }

    public async Task<IReadOnlyList<BrokerOrderEvidence>> GetOrdersAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        var request = new ListOrdersRequest { OrderStatusFilter = OrderStatusFilter.All }
            .WithInterval(new Interval<DateTime>(Utc(fromUtc), Utc(toUtc)));
        var observedAt = UtcNow;
        return (await _client.ListOrdersAsync(request, ct))
            .Select(order => Map(order, observedAt)).ToArray();
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    private static BrokerOrderEvidence Map(IOrder order, DateTime observedAt) => new(
        order.OrderId.ToString(), order.ClientOrderId ?? string.Empty, order.Symbol,
        order.OrderSide == OrderSide.Buy ? "Buy" : "Sell",
        (int)(order.Quantity ?? 0), (int)order.FilledQuantity, order.LimitPrice,
        order.AverageFillPrice, Status(order.OrderStatus), Type(order.OrderType),
        order.SubmittedAtUtc ?? observedAt, order.FilledAtUtc);

    private static string Status(OrderStatus status) => status switch
    {
        OrderStatus.New or OrderStatus.PendingNew => "Pending",
        OrderStatus.Accepted => "Accepted",
        OrderStatus.PartiallyFilled => "PartiallyFilled",
        OrderStatus.Filled => "Filled",
        OrderStatus.Canceled or OrderStatus.PendingCancel => "Cancelled",
        OrderStatus.Rejected => "Rejected",
        OrderStatus.Expired => "Expired",
        _ => "Unknown",
    };

    private static string Type(OrderType type) => type switch
    {
        OrderType.Market => "Market",
        OrderType.Limit => "Limit",
        OrderType.StopLimit => "StopLimit",
        _ => "Unknown",
    };

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    public void Dispose() => _client.Dispose();
}

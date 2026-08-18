using Alpaca.Markets;
using StockTrader.Application.Accounts;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.Broker;

/// <summary>
/// 계좌별 API 키로 초기화되는 단일 Alpaca 브로커 어댑터입니다.
/// AccountBrokerServiceFactory가 계좌 스냅샷과 공용 시계를 전달해 생성합니다.
/// </summary>
public sealed class DynamicAlpacaBrokerService : IBrokerService
{
    public BrokerType BrokerType => BrokerType.Alpaca;

    private readonly IAlpacaTradingClient _tradingClient;
    private readonly ILogger<DynamicAlpacaBrokerService> _logger;
    private readonly TimeProvider _timeProvider;

    public DynamicAlpacaBrokerService(
        string apiKey,
        string apiSecret,
        bool isPaper,
        TimeProvider timeProvider,
        ILogger<DynamicAlpacaBrokerService> logger)
    {
        _logger = logger;
        _timeProvider = timeProvider;

        // 빈 키는 더미 키로 대체 — API 호출 시 401로 실패하지만 생성 자체는 성공
        var key = string.IsNullOrWhiteSpace(apiKey) ? "DUMMY_KEY" : apiKey;
        var secret = string.IsNullOrWhiteSpace(apiSecret) ? "DUMMY_SECRET" : apiSecret;

        var secretKey = new SecretKey(key, secret);
        _tradingClient = isPaper
            ? Alpaca.Markets.Environments.Paper.GetAlpacaTradingClient(secretKey)
            : Alpaca.Markets.Environments.Live.GetAlpacaTradingClient(secretKey);
    }

    public async Task<BrokerOrder?> SubmitEntryOrderAsync(TradeRecommendation recommendation,
        CancellationToken ct = default)
    {
        if (recommendation.ShareQuantity <= 0)
        {
            _logger.LogWarning("[DynAlpaca] Cannot place order for {Symbol}: invalid quantity {Qty}",
                recommendation.Symbol, recommendation.ShareQuantity);
            return null;
        }

        // Alpaca는 소수점 2자리까지만 허용 (sub-penny 거부)
        var tp = Math.Round(recommendation.TargetPrice, 2);
        var sl = Math.Round(recommendation.StopLossPrice, 2);

        // 예외는 caller로 전파하여 실제 오류 원인이 사용자에게 노출되도록 한다.
        var order = await _tradingClient.PostOrderAsync(
            MarketOrder.Buy(recommendation.Symbol, recommendation.ShareQuantity)
                .WithDuration(TimeInForce.Day)
                .Bracket(
                    takeProfitLimitPrice: tp,
                    stopLossStopPrice: sl), ct);

        _logger.LogInformation(
            "[DynAlpaca] Order placed — {Side} {Symbol}: Qty={Qty}, OrderId={OrderId}",
            order.OrderSide, order.Symbol, order.Quantity, order.OrderId);

        return MapToModel(order);
    }

    public async Task<BrokerOrder?> IncreasePositionAsync(
        string symbol,
        int quantity,
        CancellationToken ct = default)
    {
        if (quantity <= 0 || string.IsNullOrWhiteSpace(symbol))
            return null;

        var order = await _tradingClient.PostOrderAsync(
            MarketOrder.Buy(symbol, quantity).WithDuration(TimeInForce.Day), ct);
        _logger.LogInformation(
            "[DynAlpaca] Position increased — {Symbol} {Quantity} shares, OrderId={OrderId}",
            symbol, quantity, order.OrderId);
        return MapToModel(order);
    }

    public async Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        try
        {
            if (!Guid.TryParse(orderId, out var guid))
            {
                _logger.LogWarning("[DynAlpaca] Invalid order ID format: {OrderId}", orderId);
                return false;
            }

            return await _tradingClient.CancelOrderAsync(guid, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DynAlpaca] Failed to cancel order {OrderId}", orderId);
            return false;
        }
    }

    public async Task<BrokerOrder?> ClosePositionAsync(string symbol, CancellationToken ct = default)
    {
        // 예외는 caller로 전파하여 실제 오류 원인이 사용자에게 노출되도록 한다.
        var order = await _tradingClient.DeletePositionAsync(new DeletePositionRequest(symbol), ct);
        _logger.LogInformation("[DynAlpaca] Position closed — {Symbol}", symbol);
        return MapToModel(order);
    }

    public async Task<BrokerOrder?> ClosePositionAsync(
        string symbol,
        int quantity,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            return null;

        var request = new DeletePositionRequest(symbol)
        {
            PositionQuantity = PositionQuantity.InShares(quantity),
        };
        var order = await _tradingClient.DeletePositionAsync(request, ct);
        _logger.LogInformation("[DynAlpaca] Position reduced — {Symbol} {Quantity} shares", symbol, quantity);
        return MapToModel(order);
    }

    public async Task<IReadOnlyList<BrokerPositionSnapshot>> GetPositionsAsync(
        CancellationToken ct = default)
    {
        try
        {
            var alpacaPositions = await _tradingClient.ListPositionsAsync(ct);
            return alpacaPositions.Select(p => new BrokerPositionSnapshot(
                p.Symbol,
                (int)p.IntegerQuantity,
                p.AverageEntryPrice,
                p.AssetCurrentPrice ?? p.AverageEntryPrice)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DynAlpaca] Failed to fetch positions");
            return [];
        }
    }

    public async Task<BrokerAccount?> GetAccountAsync(CancellationToken ct = default)
    {
        try
        {
            var account = await _tradingClient.GetAccountAsync(ct);
            return new BrokerAccount
            {
                AccountId = account.AccountId.ToString(),
                TotalEquity = account.Equity ?? 0m,
                Cash = account.TradableCash,
                BuyingPower = account.BuyingPower ?? 0m,
                IsTradingBlocked = account.IsTradingBlocked,
                StatusMessage = account.IsTradingBlocked ? "Trading blocked" : "Active",
                FetchedAt = _timeProvider.GetUtcNow().UtcDateTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DynAlpaca] Failed to fetch account info");
            return null;
        }
    }

    public async Task<List<BrokerOrder>> GetOrderHistoryAsync(DateTime from, DateTime to,
        CancellationToken ct = default)
    {
        try
        {
            var request = new ListOrdersRequest { OrderStatusFilter = OrderStatusFilter.All }
                .WithInterval(new Interval<DateTime>(from, to));

            var orders = await _tradingClient.ListOrdersAsync(request, ct);
            var observedAt = _timeProvider.GetUtcNow().UtcDateTime;
            return orders.Select(order => MapToModel(order, observedAt)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DynAlpaca] Failed to fetch order history");
            return [];
        }
    }

    private BrokerOrder MapToModel(IOrder o) =>
        MapToModel(o, _timeProvider.GetUtcNow().UtcDateTime);

    private static BrokerOrder MapToModel(IOrder o, DateTime observedAt) => new()
    {
        OrderId = o.OrderId.ToString(),
        Symbol = o.Symbol,
        Direction = o.OrderSide == OrderSide.Buy ? TradeDirection.Long : TradeDirection.Short,
        Quantity = (int)(o.Quantity ?? 0),
        FilledQuantity = (int)o.FilledQuantity,
        OrderPrice = o.LimitPrice,
        AverageFillPrice = o.AverageFillPrice,
        Status = MapStatus(o.OrderStatus),
        OrderType = MapOrderType(o.OrderType),
        SubmittedAt = o.SubmittedAtUtc ?? observedAt,
        FilledAt = o.FilledAtUtc
    };

    private static BrokerOrderStatus MapStatus(OrderStatus status) => status switch
    {
        OrderStatus.New or OrderStatus.PendingNew => BrokerOrderStatus.Pending,
        OrderStatus.Accepted => BrokerOrderStatus.Accepted,
        OrderStatus.PartiallyFilled => BrokerOrderStatus.PartiallyFilled,
        OrderStatus.Filled => BrokerOrderStatus.Filled,
        OrderStatus.Canceled or OrderStatus.PendingCancel => BrokerOrderStatus.Cancelled,
        OrderStatus.Rejected => BrokerOrderStatus.Rejected,
        OrderStatus.Expired => BrokerOrderStatus.Expired,
        _ => BrokerOrderStatus.Unknown
    };

    private static BrokerOrderType MapOrderType(OrderType type) => type switch
    {
        OrderType.Market => BrokerOrderType.Market,
        OrderType.Limit => BrokerOrderType.Limit,
        OrderType.StopLimit => BrokerOrderType.StopLimit,
        _ => BrokerOrderType.Unknown
    };
}

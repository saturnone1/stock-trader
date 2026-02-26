using Alpaca.Markets;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.Broker;

/// <summary>
/// 계좌별 API 키로 동적으로 초기화되는 Alpaca 브로커 서비스.
///
/// AlpacaBrokerService와 달리 IOptions에 의존하지 않으며,
/// AccountManager가 계좌별로 인스턴스를 직접 생성할 때 사용한다.
///
/// AlpacaBrokerService의 로직을 그대로 위임하되,
/// 생성자 시그니처만 달라진다 (DI 없이 직접 생성).
/// </summary>
public sealed class DynamicAlpacaBrokerService : IBrokerService
{
    private readonly IAlpacaTradingClient _tradingClient;
    private readonly ILogger<AlpacaBrokerService> _logger;

    public DynamicAlpacaBrokerService(
        string apiKey,
        string apiSecret,
        bool isPaper,
        ILogger<AlpacaBrokerService> logger)
    {
        _logger = logger;

        // 빈 키는 더미 키로 대체 — API 호출 시 401로 실패하지만 생성 자체는 성공
        var key = string.IsNullOrWhiteSpace(apiKey) ? "DUMMY_KEY" : apiKey;
        var secret = string.IsNullOrWhiteSpace(apiSecret) ? "DUMMY_SECRET" : apiSecret;

        var secretKey = new SecretKey(key, secret);
        _tradingClient = isPaper
            ? Alpaca.Markets.Environments.Paper.GetAlpacaTradingClient(secretKey)
            : Alpaca.Markets.Environments.Live.GetAlpacaTradingClient(secretKey);
    }

    public async Task<bool> PlaceOrderAsync(TradeRecommendation recommendation,
        CancellationToken ct = default)
    {
        if (recommendation.ShareQuantity <= 0)
        {
            _logger.LogWarning("[DynAlpaca] Cannot place order for {Symbol}: invalid quantity {Qty}",
                recommendation.Symbol, recommendation.ShareQuantity);
            return false;
        }

        try
        {
            var order = await _tradingClient.PostOrderAsync(
                MarketOrder.Buy(recommendation.Symbol, recommendation.ShareQuantity)
                    .WithDuration(TimeInForce.Day)
                    .Bracket(
                        takeProfitLimitPrice: recommendation.TargetPrice,
                        stopLossStopPrice: recommendation.StopLossPrice), ct);

            _logger.LogInformation(
                "[DynAlpaca] Order placed — {Side} {Symbol}: Qty={Qty}, OrderId={OrderId}",
                order.OrderSide, order.Symbol, order.Quantity, order.OrderId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DynAlpaca] Failed to place order for {Symbol}", recommendation.Symbol);
            return false;
        }
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

    public async Task<List<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        try
        {
            var alpacaPositions = await _tradingClient.ListPositionsAsync(ct);
            return alpacaPositions.Select(p => new Position
            {
                Symbol = p.Symbol,
                Quantity = (int)p.IntegerQuantity,
                EntryPrice = p.AverageEntryPrice,
                CurrentPrice = p.AssetCurrentPrice ?? p.AverageEntryPrice,
                OpenedAt = DateTime.UtcNow
            }).ToList();
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
                FetchedAt = DateTime.UtcNow
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
            return orders.Select(MapToModel).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DynAlpaca] Failed to fetch order history");
            return [];
        }
    }

    private static BrokerOrder MapToModel(IOrder o) => new()
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
        SubmittedAt = o.SubmittedAtUtc ?? DateTime.UtcNow,
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

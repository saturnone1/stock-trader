using Alpaca.Markets;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.Broker;

/// <summary>
/// Alpaca Markets 브로커 어댑터 (IBrokerService의 Alpaca 구현체).
///
/// 기존 OrderService에 있던 Alpaca SDK 의존 코드를 이곳으로 이동.
/// Alpaca SDK 타입은 이 클래스 내부에만 존재하며 외부에 노출되지 않는다.
/// </summary>
public class AlpacaBrokerService : IBrokerService
{
    private readonly IAlpacaTradingClient _tradingClient;
    private readonly ILogger<AlpacaBrokerService> _logger;

    public AlpacaBrokerService(
        IOptions<AlpacaSettings> settings,
        ILogger<AlpacaBrokerService> logger)
    {
        _logger = logger;

        var config = settings.Value;
        var secretKey = new SecretKey(config.ApiKey, config.ApiSecret);

        _tradingClient = config.IsPaper
            ? Alpaca.Markets.Environments.Paper.GetAlpacaTradingClient(secretKey)
            : Alpaca.Markets.Environments.Live.GetAlpacaTradingClient(secretKey);
    }

    /// <inheritdoc />
    public async Task<bool> PlaceOrderAsync(TradeRecommendation recommendation,
        CancellationToken ct = default)
    {
        if (recommendation.ShareQuantity <= 0)
        {
            _logger.LogWarning("[Alpaca] Cannot place order for {Symbol}: invalid quantity {Qty}",
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
                "[Alpaca] Order placed — {Side} {Symbol}: Qty={Qty}, OrderId={OrderId}, Status={Status}",
                order.OrderSide, order.Symbol, order.Quantity,
                order.OrderId, order.OrderStatus);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Alpaca] Failed to place order for {Symbol}", recommendation.Symbol);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        try
        {
            if (!Guid.TryParse(orderId, out var guid))
            {
                _logger.LogWarning("[Alpaca] Invalid order ID format: {OrderId}", orderId);
                return false;
            }

            return await _tradingClient.CancelOrderAsync(guid, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Alpaca] Failed to cancel order {OrderId}", orderId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ClosePositionAsync(string symbol, CancellationToken ct = default)
    {
        try
        {
            // Alpaca의 DeletePositionAsync는 시장가 청산 + 연결된 주문 자동 취소
            await _tradingClient.DeletePositionAsync(new DeletePositionRequest(symbol), ct);
            _logger.LogInformation("[Alpaca] Position closed — {Symbol}", symbol);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Alpaca] Failed to close position for {Symbol}", symbol);
            return false;
        }
    }

    /// <inheritdoc />
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
            _logger.LogError(ex, "[Alpaca] Failed to fetch positions");
            return [];
        }
    }

    /// <inheritdoc />
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
            _logger.LogError(ex, "[Alpaca] Failed to fetch account info");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<List<BrokerOrder>> GetOrderHistoryAsync(DateTime from, DateTime to,
        CancellationToken ct = default)
    {
        try
        {
            // Alpaca 7.x: TimeInterval은 읽기 전용 — WithInterval() 플루언트 메서드로 설정
            var request = new ListOrdersRequest { OrderStatusFilter = OrderStatusFilter.All }
                .WithInterval(new Interval<DateTime>(from, to));

            var orders = await _tradingClient.ListOrdersAsync(request, ct);
            return orders.Select(MapToModel).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Alpaca] Failed to fetch order history");
            return [];
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private static BrokerOrder MapToModel(IOrder o) => new()
    {
        OrderId = o.OrderId.ToString(),
        Symbol = o.Symbol,
        Direction = o.OrderSide == OrderSide.Buy
            ? Models.Enums.TradeDirection.Long
            : Models.Enums.TradeDirection.Short,
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

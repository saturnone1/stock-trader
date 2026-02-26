using Alpaca.Markets;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Notification;

namespace StockTrader.Services.Order;

public class OrderService : IOrderService
{
    private readonly IAlpacaTradingClient _tradingClient;
    private readonly ITradeRepository _tradeRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly INotificationService _notificationService;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOptions<AlpacaSettings> settings,
        ITradeRepository tradeRepo,
        ISettingsRepository settingsRepo,
        INotificationService notificationService,
        ILogger<OrderService> logger)
    {
        _tradeRepo = tradeRepo;
        _settingsRepo = settingsRepo;
        _notificationService = notificationService;
        _logger = logger;

        var config = settings.Value;
        var secretKey = new SecretKey(config.ApiKey, config.ApiSecret);
        _tradingClient = config.IsPaper
            ? Alpaca.Markets.Environments.Paper.GetAlpacaTradingClient(secretKey)
            : Alpaca.Markets.Environments.Live.GetAlpacaTradingClient(secretKey);
    }

    public async Task<bool> PlaceOrderAsync(TradeRecommendation recommendation,
        CancellationToken ct = default)
    {
        var userSettings = await _settingsRepo.GetAsync(ct);

        // Always save recommendation and notify
        await _tradeRepo.AddRecommendationAsync(recommendation, ct);
        _notificationService.Notify(recommendation);

        if (userSettings.OrderMode == OrderMode.AlertOnly)
        {
            _logger.LogInformation("[ALERT ONLY] {Pattern} {Symbol}: " +
                "Entry=${Entry:F2}, SL=${SL:F2}, Target=${Target:F2}, Qty={Qty}",
                recommendation.PatternType, recommendation.Symbol,
                recommendation.EntryPrice, recommendation.StopLossPrice,
                recommendation.TargetPrice, recommendation.ShareQuantity);
            return true;
        }

        // AutoOrder mode
        if (recommendation.ShareQuantity <= 0)
        {
            _logger.LogWarning("Cannot place order for {Symbol}: quantity is {Qty}",
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

            _logger.LogInformation("[ORDER PLACED] {Side} {Symbol}: " +
                "Qty={Qty}, OrderId={OrderId}, Status={Status}",
                order.OrderSide, order.Symbol, order.Quantity,
                order.OrderId, order.OrderStatus);

            recommendation.WasExecuted = true;

            var position = new Position
            {
                Symbol = recommendation.Symbol,
                Quantity = recommendation.ShareQuantity,
                EntryPrice = recommendation.EntryPrice,
                CurrentPrice = recommendation.EntryPrice,
                StopLossPrice = recommendation.StopLossPrice,
                TargetPrice = recommendation.TargetPrice,
                PatternType = recommendation.PatternType,
                OpenedAt = DateTime.UtcNow
            };
            await _tradeRepo.SavePositionAsync(position, ct);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error placing order for {Symbol}", recommendation.Symbol);
            return false;
        }
    }

    public async Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        try
        {
            if (!Guid.TryParse(orderId, out var guid))
            {
                _logger.LogWarning("Invalid order ID format: {OrderId}", orderId);
                return false;
            }
            return await _tradingClient.CancelOrderAsync(guid, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling order {OrderId}", orderId);
            return false;
        }
    }

    public async Task<List<Position>> GetOpenPositionsAsync(CancellationToken ct = default)
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
            _logger.LogError(ex, "Error fetching positions from Alpaca");
            return await _tradeRepo.GetOpenPositionsAsync(ct);
        }
    }
}

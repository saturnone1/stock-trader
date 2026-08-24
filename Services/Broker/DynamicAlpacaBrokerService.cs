using StockTrader.Application.Accounts;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.TradingCore.Broker;

namespace StockTrader.Services.Broker;

/// <summary>
/// Legacy Local-authority adapter over the same Alpaca implementation used by Trading Core.
/// It remains only for staged rollback and is disabled when Remote authority is accepted.
/// </summary>
public sealed class DynamicAlpacaBrokerService : IBrokerService, IDisposable
{
    public BrokerType BrokerType => BrokerType.Alpaca;

    private readonly AlpacaTradingBroker _broker;
    private readonly ILogger<DynamicAlpacaBrokerService> _logger;

    public DynamicAlpacaBrokerService(
        string apiKey,
        string apiSecret,
        bool isPaper,
        TimeProvider timeProvider,
        ILogger<DynamicAlpacaBrokerService> logger)
    {
        _logger = logger;
        _broker = new AlpacaTradingBroker(apiKey, apiSecret, isPaper, timeProvider);
    }

    public async Task<BrokerOrder?> SubmitEntryOrderAsync(
        TradeRecommendation recommendation,
        CancellationToken ct = default)
    {
        try
        {
            return Map(await _broker.SubmitEntryAsync(new BrokerEntryOrderRequest(
                $"st-local-{recommendation.Id}", recommendation.Symbol, recommendation.ShareQuantity,
                recommendation.TargetPrice, recommendation.StopLossPrice), ct));
        }
        catch (Exception error)
        {
            _logger.LogError(error, "[Alpaca] Protected entry failed for {Symbol}", recommendation.Symbol);
            throw;
        }
    }

    public async Task<BrokerOrder?> IncreasePositionAsync(
        string symbol,
        int quantity,
        CancellationToken ct = default) =>
        Map(await _broker.IncreasePositionAsync(new BrokerPositionOrderRequest(
            $"legacy-{Guid.NewGuid():N}", symbol, quantity), ct));

    public Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
        _broker.CancelOrderAsync(orderId, ct);

    public async Task<BrokerOrder?> ClosePositionAsync(
        string symbol,
        CancellationToken ct = default) =>
        Map(await CloseKnownQuantityAsync(symbol, null, ct));

    public async Task<BrokerOrder?> ClosePositionAsync(
        string symbol,
        int quantity,
        CancellationToken ct = default) =>
        Map(await CloseKnownQuantityAsync(symbol, quantity, ct));

    private async Task<BrokerOrderEvidence> CloseKnownQuantityAsync(
        string symbol, int? quantity, CancellationToken ct)
    {
        var resolved = quantity ?? (await _broker.GetPositionsAsync(ct))
            .FirstOrDefault(position => position.Symbol.Equals(
                symbol, StringComparison.OrdinalIgnoreCase))?.Quantity
            ?? 0;
        if (resolved <= 0)
            throw new InvalidOperationException($"No open broker position exists for {symbol}.");
        return await _broker.ClosePositionAsync(new BrokerPositionOrderRequest(
            $"legacy-{Guid.NewGuid():N}", symbol, resolved), ct);
    }

    public async Task<IReadOnlyList<BrokerPositionSnapshot>> GetPositionsAsync(
        CancellationToken ct = default) =>
        (await _broker.GetPositionsAsync(ct)).Select(position => new BrokerPositionSnapshot(
            position.Symbol, position.Quantity, position.AverageEntryPrice, position.CurrentPrice)).ToArray();

    public async Task<BrokerAccount?> GetAccountAsync(CancellationToken ct = default)
    {
        try
        {
            var account = await _broker.GetAccountAsync(ct);
            return new BrokerAccount
            {
                AccountId = account.AccountId,
                TotalEquity = account.TotalEquity,
                Cash = account.Cash,
                BuyingPower = account.BuyingPower,
                IsTradingBlocked = account.IsTradingBlocked,
                StatusMessage = account.IsTradingBlocked ? "Trading blocked" : "Active",
                FetchedAt = account.ObservedAtUtc,
            };
        }
        catch (Exception error)
        {
            _logger.LogError(error, "[Alpaca] Account evidence failed");
            return null;
        }
    }

    public async Task<List<BrokerOrder>> GetOrderHistoryAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        try
        {
            return (await _broker.GetOrdersAsync(from, to, ct)).Select(Map).ToList();
        }
        catch (Exception error)
        {
            _logger.LogError(error, "[Alpaca] Order history failed");
            return [];
        }
    }

    private static BrokerOrder Map(BrokerOrderEvidence order) => new()
    {
        OrderId = order.OrderId,
        Symbol = order.Symbol,
        Direction = order.Side == "Buy" ? TradeDirection.Long : TradeDirection.Short,
        Quantity = order.Quantity,
        FilledQuantity = order.FilledQuantity,
        OrderPrice = order.OrderPrice,
        AverageFillPrice = order.AverageFillPrice,
        Status = Enum.TryParse<BrokerOrderStatus>(order.Status, out var status)
            ? status : BrokerOrderStatus.Unknown,
        OrderType = Enum.TryParse<BrokerOrderType>(order.OrderType, out var type)
            ? type : BrokerOrderType.Unknown,
        SubmittedAt = order.SubmittedAtUtc,
        FilledAt = order.FilledAtUtc,
    };

    public void Dispose() => _broker.Dispose();
}

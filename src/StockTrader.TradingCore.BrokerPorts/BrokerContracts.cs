namespace StockTrader.TradingCore.Broker;

public sealed record BrokerEntryOrderRequest(
    string ClientOrderId,
    string Symbol,
    int Quantity,
    decimal TakeProfitPrice,
    decimal StopLossPrice);

public sealed record BrokerPositionOrderRequest(
    string ClientOrderId,
    string Symbol,
    int Quantity);

public sealed record BrokerOrderEvidence(
    string OrderId,
    string ClientOrderId,
    string Symbol,
    string Side,
    int Quantity,
    int FilledQuantity,
    decimal? OrderPrice,
    decimal? AverageFillPrice,
    string Status,
    string OrderType,
    DateTime SubmittedAtUtc,
    DateTime? FilledAtUtc);

public sealed record BrokerPositionEvidence(
    string Symbol,
    int Quantity,
    decimal AverageEntryPrice,
    decimal CurrentPrice);

public sealed record BrokerAccountEvidence(
    string AccountId,
    decimal TotalEquity,
    decimal PreviousDayEquity,
    decimal Cash,
    decimal BuyingPower,
    bool IsTradingBlocked,
    DateTime ObservedAtUtc);

public sealed record TradingBrokerConnection(
    string BrokerCode,
    string Environment,
    string ApiKey,
    string ApiSecret);

public sealed record TradingPositionRiskEvidence(string Symbol, string Sector);

public sealed record TradingRiskGateRequest(
    decimal DailyLossLimitPercent,
    int MaxTotalPositions,
    int MaxPositionsPerSector,
    string Symbol,
    string Sector,
    BrokerAccountEvidence Account,
    IReadOnlyList<BrokerPositionEvidence> BrokerPositions,
    IReadOnlyList<TradingPositionRiskEvidence> StoredPositions);

public sealed record TradingRiskGateDecision(bool Allowed, string Reason);

public static class TradingRiskGate
{
    public static TradingRiskGateDecision Evaluate(TradingRiskGateRequest request)
    {
        if (request.Account.IsTradingBlocked)
            return new(false, "broker-account-trading-blocked");
        if (request.Account.TotalEquity <= 0 || request.Account.PreviousDayEquity <= 0)
            return new(false, "broker-equity-evidence-unavailable");
        var dailyPnlPercent = (request.Account.TotalEquity - request.Account.PreviousDayEquity)
            / request.Account.PreviousDayEquity;
        if (dailyPnlPercent <= -request.DailyLossLimitPercent)
            return new(false, "daily-loss-limit-reached");
        if (request.BrokerPositions.Count >= request.MaxTotalPositions)
            return new(false, "maximum-position-count-reached");
        if (request.BrokerPositions.Any(position => position.Symbol.Equals(
                request.Symbol, StringComparison.OrdinalIgnoreCase)))
            return new(false, "symbol-position-already-open");
        if (!string.IsNullOrWhiteSpace(request.Sector)
            && request.StoredPositions.Count(position => position.Sector.Equals(
                request.Sector, StringComparison.OrdinalIgnoreCase)) >= request.MaxPositionsPerSector)
            return new(false, "maximum-sector-position-count-reached");
        return new(true, string.Empty);
    }
}

public interface ITradingBroker : IDisposable
{
    Task<BrokerOrderEvidence> SubmitEntryAsync(
        BrokerEntryOrderRequest request,
        CancellationToken ct = default);

    Task<BrokerOrderEvidence> IncreasePositionAsync(
        BrokerPositionOrderRequest request,
        CancellationToken ct = default);

    Task<BrokerOrderEvidence> ClosePositionAsync(
        BrokerPositionOrderRequest request,
        CancellationToken ct = default);

    Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default);
    Task<IReadOnlyList<BrokerPositionEvidence>> GetPositionsAsync(CancellationToken ct = default);
    Task<BrokerAccountEvidence> GetAccountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BrokerOrderEvidence>> GetOrdersAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default);
}

public interface ITradingBrokerFactory
{
    ITradingBroker Create(TradingBrokerConnection connection);
}

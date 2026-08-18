namespace StockTrader.Application.Accounts;

public sealed record ActiveBrokerAccountSnapshot(
    string AccountId,
    decimal TotalEquity,
    decimal Cash,
    decimal BuyingPower,
    decimal UnrealizedPnL,
    decimal DailyPnL,
    bool IsTradingBlocked,
    string StatusMessage,
    DateTime FetchedAt);

public interface IActiveBrokerAccountQuery
{
    Task<ActiveBrokerAccountSnapshot?> GetAsync(CancellationToken ct = default);
}

using StockTrader.Application.Accounts;

namespace StockTrader.Services.Account;

public sealed class ActiveBrokerAccountQuery(
    IAccountManager accountManager,
    ILogger<ActiveBrokerAccountQuery> logger) : IActiveBrokerAccountQuery
{
    public async Task<ActiveBrokerAccountSnapshot?> GetAsync(
        CancellationToken ct = default)
    {
        try
        {
            var broker = await accountManager.GetActiveBrokerServiceAsync(ct);
            var account = broker is null ? null : await broker.GetAccountAsync(ct);
            return account is null
                ? null
                : new ActiveBrokerAccountSnapshot(
                    account.AccountId,
                    account.TotalEquity,
                    account.Cash,
                    account.BuyingPower,
                    account.UnrealizedPnL,
                    account.DailyPnL,
                    account.IsTradingBlocked,
                    account.StatusMessage,
                    account.FetchedAt);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not fetch active broker account snapshot");
            return null;
        }
    }
}

using StockTrader.Application.Reporting;

namespace StockTrader.Services.Account;

public sealed class ActiveAccountEquityReader(
    IAccountManager accountManager,
    ILogger<ActiveAccountEquityReader> logger) : IActiveAccountEquityReader
{
    public async Task<decimal?> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var broker = await accountManager.GetActiveBrokerServiceAsync(ct);
            var account = broker is null ? null : await broker.GetAccountAsync(ct);
            return account?.TotalEquity is > 0m ? account.TotalEquity : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not fetch active account equity for daily report");
            return null;
        }
    }
}

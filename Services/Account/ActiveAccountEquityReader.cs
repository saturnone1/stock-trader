using StockTrader.Application.Reporting;
using StockTrader.Application.Accounts;

namespace StockTrader.Services.Account;

public sealed class ActiveAccountEquityReader(
    IActiveBrokerAccountQuery accountQuery) : IActiveAccountEquityReader
{
    public async Task<decimal?> GetAsync(CancellationToken ct = default)
    {
        var account = await accountQuery.GetAsync(ct);
        return account?.TotalEquity is > 0m ? account.TotalEquity : null;
    }
}

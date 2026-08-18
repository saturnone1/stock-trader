using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StockTrader.Application.Risk;
using StockTrader.Application.Trading;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Services.Account;

namespace StockTrader.Services.Risk;

public sealed class RiskManagementDataSource(
    IAccountManager accountManager,
    IOpenPositionStore positions,
    ISettingsRepository settings,
    IMemoryCache cache,
    IOptions<TradingSettings> tradingOptions,
    ILogger<RiskManagementDataSource> logger) : IRiskManagementDataSource
{
    private const string OpenPositionsCacheKey = "risk:open_positions";
    private readonly TimeSpan _openPositionsCacheTtl = TimeSpan.FromSeconds(
        tradingOptions.Value.RiskOpenPositionCacheSeconds);

    public async Task<int?> GetActiveAccountIdAsync(CancellationToken ct = default) =>
        (await accountManager.GetActiveAccountAsync(ct))?.Id;

    public async Task<IReadOnlyList<RiskOpenPosition>> GetOpenPositionsAsync(
        CancellationToken ct = default) =>
        await cache.GetOrCreateAsync(OpenPositionsCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _openPositionsCacheTtl;
            return await LoadPositionsAsync(ct);
        }) ?? [];

    public async Task<RiskPortfolioEvidence> LoadPortfolioEvidenceAsync(
        CancellationToken ct = default)
    {
        var userSettings = await settings.GetAsync(ct);
        var openPositions = await LoadPositionsAsync(ct);
        var accounts = await accountManager.GetAllAccountsAsync(ct);
        var evidence = new List<RiskAccountEvidence>();

        foreach (var account in accounts.Where(account => account.IsEnabled))
        {
            RiskAccountBalance? balance = null;
            try
            {
                var broker = await accountManager.GetBrokerServiceForAccountAsync(
                    account.Id,
                    ct);
                var brokerAccount = broker is null
                    ? null
                    : await broker.GetAccountAsync(ct);
                if (brokerAccount is not null)
                {
                    balance = new RiskAccountBalance(
                        brokerAccount.TotalEquity,
                        brokerAccount.DailyPnL);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogDebug(
                    ex,
                    "Could not fetch account info for risk calc, using fallback for [{Id}]",
                    account.Id);
            }

            evidence.Add(new RiskAccountEvidence(account.Id, balance));
        }

        return new RiskPortfolioEvidence(
            userSettings.AccountSize,
            openPositions,
            evidence);
    }

    private async Task<IReadOnlyList<RiskOpenPosition>> LoadPositionsAsync(
        CancellationToken ct)
    {
        var stored = await positions.GetOpenPositionsAsync(ct);
        return stored.Select(position => new RiskOpenPosition(
            position.AccountId,
            position.Symbol,
            position.Sector,
            position.UnrealizedPnL)).ToArray();
    }
}

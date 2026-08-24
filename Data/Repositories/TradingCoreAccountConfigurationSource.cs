using Microsoft.EntityFrameworkCore;
using StockTrader.Application.TradingCore;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.TradingCore;
using StockTrader.Configuration;
using Microsoft.Extensions.Options;

namespace StockTrader.Data.Repositories;

internal sealed class TradingCoreAccountConfigurationSource(
    IDbContextFactory<AppDbContext> dbFactory,
    TradingAccountConfigurationGenerationState generationState,
    IOptions<TradingSettings> tradingOptions)
    : ITradingCoreAccountConfigurationSource
{
    public async Task<TradingAccountConfigurationSet> CaptureAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var accounts = await db.TradingAccounts.AsNoTracking().OrderBy(item => item.Id)
            .Select(item => new TradingAccountConfiguration(
                item.Id.ToString(), item.BrokerType.ToString(), item.Environment,
                item.IsEnabled, item.IsActive, item.ApiKey, item.ApiSecret))
            .ToArrayAsync(ct);
        var risk = new TradingRiskConfiguration(
            tradingOptions.Value.RiskPerTradePercent,
            tradingOptions.Value.DailyLossLimitPercent,
            tradingOptions.Value.MaxTotalPositions,
            tradingOptions.Value.MaxPositionsPerSector);
        var contentHash = CanonicalJsonHash.Compute(new { Accounts = accounts.Select(account => new
        {
            account.AccountId,
            account.BrokerCode,
            account.Environment,
            account.IsEnabled,
            account.IsActive,
            ApiKey = CanonicalJsonHash.Compute(account.ApiKey),
            ApiSecret = CanonicalJsonHash.Compute(account.ApiSecret),
        }).ToArray(), Risk = risk });
        var (generation, issuedAt) = generationState.Resolve(contentHash);
        var configuration = new TradingAccountConfigurationSet(
            TradingCoreContractVersions.Current, generation, string.Empty, issuedAt, accounts, risk);
        return configuration with
        {
            ConfigurationHash = TradingCoreIdentity.AccountConfiguration(configuration)
        };
    }
}

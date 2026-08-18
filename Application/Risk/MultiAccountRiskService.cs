using System.Collections.Frozen;
using StockTrader.Application.Execution;

namespace StockTrader.Application.Risk;

internal sealed class MultiAccountRiskService(
    IRiskManagementDataSource dataSource,
    RiskStateStore stateStore,
    RiskManagementOptions options,
    TimeProvider timeProvider,
    ILogger<MultiAccountRiskService> logger) : IRiskManagementService
{
    public async Task<RiskStateSnapshot> GetCurrentRiskStateAsync(
        CancellationToken ct = default)
    {
        var activeAccountId = await dataSource.GetActiveAccountIdAsync(ct);
        var state = stateStore.Snapshot();
        return activeAccountId.HasValue
            && state.Accounts.TryGetValue(activeAccountId.Value, out var account)
                ? account
                : state.Fallback;
    }

    internal RiskStateSnapshot GetPortfolioRiskState() =>
        stateStore.Snapshot().Portfolio;

    internal RiskStateSnapshot GetAccountRiskState(int accountId)
    {
        var state = stateStore.Snapshot();
        return state.Accounts.TryGetValue(accountId, out var account)
            ? account
            : RiskStateStore.Empty();
    }

    public async Task<(bool Allowed, string Reason)> CanOpenPositionAsync(
        string symbol,
        string sector,
        CancellationToken ct = default)
    {
        var activeAccountId = await dataSource.GetActiveAccountIdAsync(ct);
        if (!activeAccountId.HasValue)
            return (false, "활성 계좌가 없습니다. 계좌 관리에서 계좌를 추가하세요.");

        var state = stateStore.Snapshot();
        if (state.Accounts.TryGetValue(activeAccountId.Value, out var risk)
            && risk.IsTradingHalted)
        {
            return (false, "Trading halted: daily loss limit reached");
        }

        var allOpenPositions = await dataSource.GetOpenPositionsAsync(ct);
        var accountPositions = allOpenPositions
            .Where(position => position.AccountId == activeAccountId.Value
                || position.AccountId == 0)
            .ToArray();

        if (accountPositions.Length >= options.MaxTotalPositions)
            return (false, $"Max total positions ({options.MaxTotalPositions}) reached");

        if (accountPositions.Any(position => string.Equals(
                position.Symbol,
                symbol,
                StringComparison.OrdinalIgnoreCase)))
        {
            return (false, $"Already have open position in {symbol}");
        }

        if (!string.IsNullOrEmpty(sector))
        {
            var sectorCount = accountPositions.Count(position => string.Equals(
                position.Sector,
                sector,
                StringComparison.OrdinalIgnoreCase));
            if (sectorCount >= options.MaxPositionsPerSector)
            {
                return (false,
                    $"Max positions per sector ({options.MaxPositionsPerSector}) reached for {sector}");
            }
        }

        return (true, string.Empty);
    }

    public decimal CalculatePositionSize(
        decimal accountSize,
        decimal riskPercent,
        decimal entryPrice,
        decimal stopLossPrice) =>
        LongPositionSizingPolicy.CalculateRiskCapital(
            accountSize,
            riskPercent,
            entryPrice,
            stopLossPrice);

    public async Task UpdateDailyPnLAsync(CancellationToken ct = default)
    {
        var evidence = await dataSource.LoadPortfolioEvidenceAsync(ct);
        var observedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (evidence.EnabledAccounts.Count == 0)
        {
            var fallback = BuildFallbackState(evidence, observedAt);
            stateStore.Publish(new RiskStateGeneration(
                new Dictionary<int, RiskStateSnapshot>().ToFrozenDictionary(),
                fallback,
                fallback));
            return;
        }

        var legacyPositionAccountId = evidence.EnabledAccounts[0].AccountId;
        var accountStates = new Dictionary<int, RiskStateSnapshot>();
        decimal totalPnl = 0m;
        decimal totalAccountSize = 0m;

        foreach (var account in evidence.EnabledAccounts)
        {
            var accountPositions = evidence.OpenPositions
                .Where(position => position.AccountId == account.AccountId
                    || (position.AccountId == 0
                        && account.AccountId == legacyPositionAccountId))
                .ToArray();
            var accountSize = account.Balance is { TotalEquity: > 0m }
                ? account.Balance.TotalEquity
                : evidence.DefaultAccountSize;
            var effectivePnl = account.Balance?.DailyPnl
                ?? accountPositions.Sum(position => position.UnrealizedPnl);
            var state = BuildState(accountPositions, accountSize, effectivePnl, observedAt);
            accountStates.Add(account.AccountId, state);

            if (state.IsTradingHalted)
            {
                logger.LogWarning(
                    "[Account {Id}] TRADING HALTED: Daily loss {PnL:P2} exceeds limit {Limit:P2}",
                    account.AccountId,
                    state.DailyPnLPercent,
                    -options.DailyLossLimitPercent);
            }

            totalPnl += effectivePnl;
            totalAccountSize += accountSize;
        }

        var portfolio = BuildState(
            evidence.OpenPositions,
            totalAccountSize,
            totalPnl,
            observedAt);
        stateStore.Publish(new RiskStateGeneration(
            accountStates.ToFrozenDictionary(),
            portfolio,
            RiskStateStore.Empty(observedAt)));
    }

    private RiskStateSnapshot BuildFallbackState(
        RiskPortfolioEvidence evidence,
        DateTime observedAt)
    {
        var dailyPnl = evidence.OpenPositions.Sum(position => position.UnrealizedPnl);
        return BuildState(
            evidence.OpenPositions,
            evidence.DefaultAccountSize,
            dailyPnl,
            observedAt);
    }

    private RiskStateSnapshot BuildState(
        IReadOnlyList<RiskOpenPosition> positions,
        decimal accountSize,
        decimal dailyPnl,
        DateTime observedAt)
    {
        var pnlPercent = accountSize > 0m ? dailyPnl / accountSize : 0m;
        var sectors = positions
            .Where(position => !string.IsNullOrEmpty(position.Sector))
            .GroupBy(position => position.Sector!, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(group => group.Key, group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        return new RiskStateSnapshot(
            dailyPnl,
            pnlPercent,
            pnlPercent <= -options.DailyLossLimitPercent,
            positions.Count,
            sectors,
            observedAt);
    }
}

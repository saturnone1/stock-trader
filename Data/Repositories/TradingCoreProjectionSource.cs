using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using StockTrader.Application.Risk;
using StockTrader.Application.TradingCore;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Data.Repositories;

internal sealed class TradingCoreProjectionSource(
    IDbContextFactory<AppDbContext> dbFactory,
    IRiskManagementService riskManagement,
    TimeProvider clock) : ITradingCoreProjectionSource
{
    private static readonly JsonSerializerOptions ContractJson =
        new(JsonSerializerDefaults.Web);

    public async Task<TradingStateSnapshot> CaptureAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var accounts = await db.TradingAccounts.AsNoTracking().OrderBy(item => item.Id).ToListAsync(ct);
        var recommendations = await db.TradeRecommendations.AsNoTracking()
            .Where(item => !item.IsSuperseded).OrderBy(item => item.Id).ToListAsync(ct);
        var positions = await db.Positions.AsNoTracking().Include(item => item.ScalingExecutions)
            .OrderBy(item => item.Id).ToListAsync(ct);
        var trades = await db.TradeRecords.AsNoTracking().OrderBy(item => item.Id).ToListAsync(ct);
        var risk = await riskManagement.GetCurrentRiskStateAsync(ct);
        var captured = Utc(clock.GetUtcNow().UtcDateTime);
        var snapshot = new TradingStateSnapshot(
            TradingCoreContractVersions.Current,
            string.Empty,
            captured.Ticks,
            captured,
            accounts.Select(item => new TradingAccountProjection(
                item.Id.ToString(), item.BrokerType.ToString(), item.Environment,
                item.IsEnabled, item.IsActive, Utc(item.UpdatedAt).Ticks)).ToArray(),
            recommendations.Select(item => new TradingRecommendationProjection(
                item.Id.ToString(), item.SourceSignalId?.ToString() ?? $"legacy:{item.Id}",
                item.Symbol, item.PatternType.ToString(), item.CustomPatternName,
                Utc(item.GeneratedAt), item.EntryPrice, item.StopLossPrice, item.TargetPrice,
                item.ShareQuantity, item.Expectancy, item.Mode.ToString(), item.WasExecuted,
                Utc(item.EntryRequestedAt), item.EntryAccountId?.ToString(),
                item.EntryOrderId, item.EntryExecutionNote)).ToArray(),
            positions.Select(item => new TradingPositionProjection(
                item.Id.ToString(), item.SourceSignalId?.ToString(), item.AccountId.ToString(),
                item.Symbol, item.Sector, item.Quantity, item.InitialQuantity, item.EntryPrice,
                item.CurrentPrice, item.StopLossPrice, item.TargetPrice, item.PatternType.ToString(),
                item.CustomPatternName, Utc(item.OpenedAt), Utc(item.ClosedAt), item.ExitPrice,
                item.HighSinceEntry, item.EntryAtr, item.InitialRiskDistance, item.BreakevenApplied,
                item.TrailingStopActivated, item.PartialProfitTaken, Utc(item.ExecutionRequestedAt),
                item.ExecutionRequestReason, item.ExecutionRequestQuantity,
                item.ExecutionRequestMarksPartialProfit, item.ExecutionRequestKind?.ToString(),
                item.ExecutionRequestRuleIndex, item.ExecutionOrderId,
                item.ScalingExecutions.OrderBy(scale => scale.RuleIndex)
                    .Select(scale => new TradingScalingProjection(scale.RuleIndex, scale.ExecutionCount))
                    .ToArray(), ExecutionContext(item), item.LastEvaluatedEvidenceId,
                Utc(item.LastEvaluatedBarUtc), item.LastEvaluatedMarketDataRevision)).ToArray(),
            trades.Select(item => new TradingTradeProjection(
                item.Id.ToString(), item.SourceSignalId?.ToString(), item.Symbol,
                item.PatternType.ToString(), item.CustomPatternName, item.EntryPrice, item.ExitPrice,
                item.Quantity, Utc(item.EntryTime), Utc(item.ExitTime), item.PnL,
                item.PnLPercent, item.ExitReason)).ToArray(),
            new TradingRiskProjection(risk.DailyPnL, risk.DailyPnLPercent,
                risk.OpenPositionCount, risk.IsTradingHalted, Utc(risk.LastUpdated)));
        return snapshot with { SnapshotId = TradingCoreIdentity.Snapshot(snapshot) };
    }

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static DateTime? Utc(DateTime? value) => value.HasValue ? Utc(value.Value) : null;

    private static TradingPositionExecutionContext? ExecutionContext(Models.Position position)
    {
        if (string.IsNullOrWhiteSpace(position.ExecutionArtifactJson)
            || string.IsNullOrWhiteSpace(position.EntryMarketDataEvidenceJson))
            return null;
        try
        {
            var artifact = JsonSerializer.Deserialize<TradingStrategyExecutionArtifact>(
                position.ExecutionArtifactJson, ContractJson);
            var evidence = JsonSerializer.Deserialize<StockTrader.ServiceContracts.MarketData.MarketDataEvidenceContract>(
                position.EntryMarketDataEvidenceJson, ContractJson);
            return artifact is null || evidence is null
                ? null
                : new TradingPositionExecutionContext(artifact, evidence);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

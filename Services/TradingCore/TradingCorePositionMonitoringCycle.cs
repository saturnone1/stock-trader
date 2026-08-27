using Microsoft.Extensions.Options;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Application.Trading;
using StockTrader.Application.TradingCore;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models.Enums;
using StockTrader.Models;
using StockTrader.Services.Order;
using StockTrader.Services.Patterns;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Services.TradingCore;

/// <summary>
/// Evaluates immutable position semantics in the API compute boundary while every financial claim,
/// broker call, fill, and canonical mutation remains exclusively owned by Trading Core.
/// </summary>
internal sealed class TradingCorePositionMonitoringCycle(
    ITradingCoreControlPlane core,
    IOhlcvRepository bars,
    ILiveDailyScanData marketData,
    TradingPositionExecutionContextResolver contexts,
    LivePositionExecutionEvaluator evaluator,
    IOptions<TradingSettings> trading,
    TimeProvider clock,
    ILogger<TradingCorePositionMonitoringCycle> logger) : ILivePositionMonitoringCycle
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var status = await core.GetStatusAsync(ct);
        if (!status.Ready || status.Mode != TradingAuthorityMode.Remote)
            throw new InvalidOperationException("trading-core-remote-authority-unavailable");
        var portfolio = await core.GetPortfolioAsync(ct);
        foreach (var projection in portfolio.Positions.Where(value => value.ClosedAtUtc is null))
        {
            if (projection.ExecutionContext is null || await HasPendingCommandAsync(projection, ct))
                continue;
            try
            {
                var resolved = contexts.Resolve(projection.ExecutionContext);
                if (resolved.Strategy?.ReferenceSymbols.Any(symbol =>
                        !symbol.Equals(projection.Symbol, StringComparison.OrdinalIgnoreCase)) == true)
                    throw new InvalidOperationException(
                        "remote-position-reference-series-evidence-not-supported");
                var position = TradingCoreProjectionMapper.Position(projection);
                if (position.CurrentPrice <= 0)
                    continue;
                var account = portfolio.Accounts.SingleOrDefault(value =>
                    value.AccountId.Equals(projection.AccountId, StringComparison.Ordinal));
                if (account is null || account.TotalEquity <= 0)
                    throw new InvalidOperationException(
                        "position-broker-account-evidence-unavailable");
                var now = clock.GetUtcNow().UtcDateTime;
                var lookbackDays = resolved.Settings.Tqqq200Sma is { } tqqq
                    ? Math.Max(
                        StrategyEvaluationPolicy.LivePositionIndicatorLookbackDays,
                        Tqqq200SmaExecutionPolicy.RequiredCalendarLookbackDays(tqqq.SmaPeriod))
                    : StrategyEvaluationPolicy.LivePositionIndicatorLookbackDays;
                var evidence = await marketData.LoadBarsAsync(
                    projection.Symbol,
                    now.AddDays(-lookbackDays),
                    now,
                    ct);
                if (!evidence.Evidence.IsComplete)
                    throw new InvalidOperationException("incomplete-position-market-data-evidence");
                var decision = await evaluator.EvaluateImmutableAsync(
                    position,
                    resolved.Strategy,
                    bars,
                    resolved.Settings,
                    evidence.Bars,
                    ct,
                    account.TotalEquity,
                    trading.Value.MaxTotalPositions);
                if (PolicyStateChanged(projection, position))
                {
                    var stateReceipt = await core.UpdatePositionStateAsync(
                        TradingCorePositionCommandFactory.CreateStateUpdate(
                            status, projection, position, evidence.Evidence, now),
                        ct);
                    if (stateReceipt.Status != TradingCommandStatuses.Completed)
                        throw new InvalidOperationException("position-policy-state-update-not-completed");
                }
                if (!decision.ShouldExecute)
                    continue;
                var intent = decision.Intent!;
                var command = TradingCorePositionCommandFactory.Create(
                    status,
                    projection,
                    Action(intent.Kind),
                    intent.Quantity,
                    intent.Reason,
                    evidence.Evidence,
                    now,
                    intent.ScalingRuleIndex,
                    intent.MarksPartialProfit);
                var receipt = await core.SubmitPositionAsync(command, ct);
                logger.LogInformation(
                    "Trading Core position decision {CommandId} for {Symbol}: {Action}/{Status}",
                    receipt.CommandId,
                    projection.Symbol,
                    command.Action,
                    receipt.Status);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogError(error,
                    "Trading Core position evaluation failed for {PositionId}/{Symbol}",
                    projection.PositionId,
                    projection.Symbol);
            }
        }
    }

    private static bool PolicyStateChanged(
        TradingPositionProjection projection,
        Position evaluated) =>
        projection.HighSinceEntry != evaluated.HighSinceEntry
        || projection.StopLossPrice != evaluated.StopLossPrice
        || projection.InitialRiskDistance != evaluated.InitialRiskDistance
        || projection.BreakevenApplied != evaluated.BreakevenApplied
        || projection.TrailingStopActivated != evaluated.TrailingStopActivated;

    private async Task<bool> HasPendingCommandAsync(
        TradingPositionProjection position,
        CancellationToken ct)
    {
        var command = await core.GetLatestPositionCommandAsync(position.PositionId, ct);
        return command?.Status is TradingCommandStatuses.PendingBrokerSubmission
            or TradingCommandStatuses.AwaitingBrokerEvidence
            or TradingCommandStatuses.ReconciliationRequired;
    }

    private static string Action(PositionExecutionKind kind) => kind switch
    {
        PositionExecutionKind.FullExit => TradingPositionActionKinds.FullExit,
        PositionExecutionKind.PartialProfit => TradingPositionActionKinds.PartialExit,
        PositionExecutionKind.ScaleIn => TradingPositionActionKinds.ScaleIn,
        PositionExecutionKind.ScaleOut => TradingPositionActionKinds.ScaleOut,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

using Microsoft.Extensions.Options;
using StockTrader.Application.Execution;
using StockTrader.Application.Settings;
using StockTrader.Application.Strategies;
using StockTrader.Application.Trading;
using StockTrader.Application.TradingCore;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.TradingCore;
using StockTrader.Services.Order;

namespace StockTrader.Services.TradingCore;

/// <summary>
/// Replays projected Local position decisions with immutable entry semantics and exact market-data
/// evidence. It writes only Shadow comparison records through the control plane.
/// </summary>
internal sealed class TradingCorePositionShadowCycle(
    ITradingCoreControlPlane core,
    IOhlcvRepository bars,
    ILiveDailyScanData marketData,
    TradingPositionExecutionContextResolver contexts,
    LivePositionExecutionEvaluator evaluator,
    ISettingsRepository settings,
    IOptions<TradingSettings> trading,
    TimeProvider clock,
    ILogger<TradingCorePositionShadowCycle> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var status = await core.GetStatusAsync(ct);
        if (!status.Ready || status.Mode != TradingAuthorityMode.Shadow)
            throw new InvalidOperationException("trading-core-shadow-authority-unavailable");
        var portfolio = await core.GetPortfolioAsync(ct);
        var accountSize = (await settings.GetAsync(ct)).AccountSize;
        foreach (var projection in portfolio.Positions.Where(value =>
                     value.ClosedAtUtc is null && value.ExecutionContext is not null))
        {
            try
            {
                var resolved = contexts.Resolve(projection.ExecutionContext!);
                if (resolved.Strategy?.ReferenceSymbols.Any(symbol =>
                        !symbol.Equals(projection.Symbol, StringComparison.OrdinalIgnoreCase)) == true)
                    throw new InvalidOperationException(
                        "shadow-position-reference-series-evidence-not-supported");
                var now = clock.GetUtcNow().UtcDateTime;
                var lookbackDays = resolved.Settings.Tqqq200Sma is { } tqqq
                    ? Math.Max(
                        StrategyEvaluationPolicy.LivePositionIndicatorLookbackDays,
                        Tqqq200SmaExecutionPolicy.RequiredCalendarLookbackDays(tqqq.SmaPeriod))
                    : StrategyEvaluationPolicy.LivePositionIndicatorLookbackDays;
                var evidence = await marketData.LoadBarsAsync(
                    projection.Symbol, now.AddDays(-lookbackDays), now, ct);
                if (!evidence.Evidence.IsComplete)
                    throw new InvalidOperationException("incomplete-position-market-data-evidence");
                var position = TradingCoreProjectionMapper.Position(projection);
                var authoritativePolicyState = PolicyState(projection);
                var decision = await evaluator.EvaluateImmutableAsync(
                    position, resolved.Strategy, bars, resolved.Settings, evidence.Bars, ct,
                    accountSize, trading.Value.MaxTotalPositions);
                var authoritative = Authoritative(projection);
                var candidate = Candidate(decision);
                var observation = new TradingShadowPositionObservation(
                    TradingCoreContractVersions.Current, string.Empty, string.Empty, now,
                    projection.PositionId, CanonicalJsonHash.Compute(projection),
                    projection.ExecutionContext!.ExecutionArtifact.ArtifactId,
                    evidence.Evidence,
                    authoritative.Disposition, authoritative.Action, authoritative.Quantity,
                    authoritative.Reason, authoritativePolicyState,
                    candidate.Disposition, candidate.Action, candidate.Quantity, candidate.Reason,
                    PolicyState(position));
                var hash = TradingCoreIdentity.ShadowPositionPayload(observation);
                observation = observation with
                {
                    DecisionId = $"shadow-position:{hash}",
                    PayloadHash = hash,
                };
                var receipt = await core.CompareShadowPositionAsync(observation, ct);
                if (!receipt.IsMatch)
                    logger.LogError(
                        "Trading Core position Shadow mismatch {PositionId}: Local={Local}/{LocalAction}/{LocalQuantity} Candidate={Candidate}/{CandidateAction}/{CandidateQuantity}",
                        projection.PositionId, receipt.AuthoritativeDisposition,
                        receipt.AuthoritativeAction, receipt.AuthoritativeQuantity,
                        receipt.CandidateDisposition, receipt.CandidateAction,
                        receipt.CandidateQuantity);
                else
                    logger.LogInformation(
                        "Trading Core position Shadow parity confirmed for {PositionId}: {Disposition}/{Action}/{Quantity}",
                        projection.PositionId, receipt.CandidateDisposition,
                        receipt.CandidateAction, receipt.CandidateQuantity);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogError(error,
                    "Trading Core position Shadow evaluation failed for {PositionId}/{Symbol}",
                    projection.PositionId, projection.Symbol);
            }
        }
    }

    private static ShadowPositionDecision Authoritative(TradingPositionProjection position)
    {
        if (!position.ExecutionRequestedAtUtc.HasValue)
            return ShadowPositionDecision.None;
        var action = position.ExecutionRequestKind switch
        {
            null or "FullExit" => TradingPositionActionKinds.FullExit,
            "PartialProfit" => TradingPositionActionKinds.PartialExit,
            "ScaleIn" => TradingPositionActionKinds.ScaleIn,
            "ScaleOut" => TradingPositionActionKinds.ScaleOut,
            var value => throw new InvalidOperationException(
                $"unsupported-local-position-execution-kind:{value}"),
        };
        return new(TradingShadowDispositions.PositionCommand, action,
            position.ExecutionRequestQuantity ?? position.Quantity,
            position.ExecutionRequestReason);
    }

    private static ShadowPositionDecision Candidate(LivePositionExecutionDecision decision)
    {
        if (!decision.ShouldExecute)
            return ShadowPositionDecision.None;
        var intent = decision.Intent!;
        return new(TradingShadowDispositions.PositionCommand, intent.Kind switch
        {
            PositionExecutionKind.FullExit => TradingPositionActionKinds.FullExit,
            PositionExecutionKind.PartialProfit => TradingPositionActionKinds.PartialExit,
            PositionExecutionKind.ScaleIn => TradingPositionActionKinds.ScaleIn,
            PositionExecutionKind.ScaleOut => TradingPositionActionKinds.ScaleOut,
            _ => throw new ArgumentOutOfRangeException(nameof(intent.Kind), intent.Kind, null),
        }, intent.Quantity, intent.Reason);
    }

    private static TradingShadowPositionPolicyState PolicyState(
        TradingPositionProjection position) => new(
        position.HighSinceEntry,
        position.StopLossPrice,
        position.InitialRiskDistance,
        position.BreakevenApplied,
        position.TrailingStopActivated);

    private static TradingShadowPositionPolicyState PolicyState(Position position) => new(
        position.HighSinceEntry,
        position.StopLossPrice,
        position.InitialRiskDistance,
        position.BreakevenApplied,
        position.TrailingStopActivated);

    private sealed record ShadowPositionDecision(
        string Disposition,
        string? Action,
        int? Quantity,
        string? Reason)
    {
        public static ShadowPositionDecision None { get; } =
            new(TradingShadowDispositions.NoAction, null, null, null);
    }
}

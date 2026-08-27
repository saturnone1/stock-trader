using System.Text.Json;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Domain.MarketData;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Optimization.Protocol;
using StockTrader.ServiceContracts.Optimization;
using StockTrader.ServiceContracts.MarketData;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Services.TradingCore;

/// <summary>
/// Captures the exact strategy semantics accepted by financial execution. The snapshot is owned by
/// the command and must never be re-resolved from mutable settings while a position is open.
/// </summary>
public static class TradingExecutionArtifactFactory
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TradingStrategyExecutionArtifact Create(
        PatternSignal signal,
        CompiledStrategy? customStrategy,
        PatternSettings settings,
        PatternParameterOverrides? overrides)
    {
        var patternCode = signal.PatternType.ToString();
        if (customStrategy is not null)
        {
            var strategy = StrategyExecutionArtifactFactory.Create(customStrategy.Source);
            var exitPolicy = LongPositionExitPolicyCatalog.ForCustom(customStrategy.Source);
            return CreateArtifact(
                TradingExecutionArtifactKinds.StrategyDocument,
                patternCode,
                strategy,
                "{}",
                strategy.CalendarVersion,
                PositionManagement(exitPolicy, minimumRequiredBars: CustomRequiredBars(customStrategy)));
        }

        var snapshot = new BuiltInPatternExecutionSnapshot(
            PatternConfiguration(signal.PatternType, settings),
            LongPositionExitPolicyCatalog.ForPattern(signal.PatternType, overrides));
        var snapshotJson = JsonSerializer.Serialize(snapshot, Json);
        return CreateArtifact(
            TradingExecutionArtifactKinds.BuiltInPattern,
            patternCode,
            null,
            snapshotJson,
            MarketCalendarVersion.Current,
            PositionManagement(
                snapshot.ExitPolicy,
                cumulativeRsi: signal.PatternType == PatternType.CumulativeRsi2
                    ? new TradingCumulativeRsiExitPolicy(
                        settings.CumulativeRsi2.RsiPeriod,
                        settings.CumulativeRsi2.CumulativePeriod,
                        settings.CumulativeRsi2.ExitThreshold,
                        settings.CumulativeRsi2.LongTrendMaPeriod)
                    : null,
                trendStop: signal.PatternType == PatternType.Tqqq200Sma
                    ? new TradingTrendStopPolicy(
                        settings.Tqqq200Sma.SmaPeriod,
                        settings.Tqqq200Sma.SmaStopMultiplier)
                    : null));
    }

    private static TradingStrategyExecutionArtifact CreateArtifact(
        string kind,
        string patternCode,
        StrategyExecutionArtifact? strategy,
        string builtInSettingsJson,
        string calendarVersion,
        TradingPositionManagementArtifact positionManagement)
    {
        var hash = TradingExecutionArtifactPolicy.ComputeDefinitionHash(
            kind, patternCode, strategy, builtInSettingsJson, calendarVersion, positionManagement);
        return new TradingStrategyExecutionArtifact(
            TradingCoreContractVersions.Current,
            hash,
            kind,
            patternCode,
            strategy,
            builtInSettingsJson,
            hash,
            OptimizationWorkerContractCatalog.EngineSemanticsVersion,
            OptimizationWorkerContractCatalog.PatternCatalogVersion,
            calendarVersion,
            CanOpenPosition: true,
            CanManagePosition: true,
            PositionManagement: positionManagement);
    }

    private static TradingPositionManagementArtifact PositionManagement(
        LongPositionExitPolicy policy,
        int minimumRequiredBars = 0,
        TradingCumulativeRsiExitPolicy? cumulativeRsi = null,
        TradingTrendStopPolicy? trendStop = null)
    {
        var requiredBars = Math.Max(
            minimumRequiredBars,
            Math.Max(StrategyEvaluationPolicy.MinimumWarmupBars,
                Math.Max(cumulativeRsi?.LongTrendMovingAveragePeriod ?? 0,
                    trendStop?.MovingAveragePeriod ?? 0) + 20));
        if (requiredBars > MarketDataExecutionEvidenceLimits.MaximumBars)
            throw new InvalidOperationException(
                "live-position-required-bars-exceed-execution-evidence-limit");
        return new TradingPositionManagementArtifact(
            new TradingLongPositionPolicy(
                policy.MaxHoldingBars,
                policy.EnableTrailingStop,
                policy.TrailingStopAtrMultiplier,
                policy.TrailingActivationR,
                policy.EnablePartialProfit,
                policy.PartialProfitRMultiple,
                policy.EnableTargetExit,
                policy.EnableTimeExit,
                policy.BreakevenAtrMultiplier,
                policy.StopReason,
                policy.ProtectedStopReason),
            RequiredBars: requiredBars,
            cumulativeRsi,
            trendStop);
    }

    private static int CustomRequiredBars(CompiledStrategy strategy)
    {
        var rules = strategy.ExitRules
            .Concat(strategy.ExitGroups.SelectMany(group => group.Rules))
            .Concat(strategy.ScalingRules.SelectMany(rule => rule.Conditions));
        return rules.Select(RuleRequiredBars)
            .DefaultIfEmpty(StrategyEvaluationPolicy.MinimumWarmupBars)
            .Append(StrategyEvaluationPolicy.MinimumWarmupBars)
            .Max();
    }

    private static int RuleRequiredBars(EntryRule rule)
    {
        var primary = IndicatorCatalog.RequiredBars(rule.Indicator, rule.Params);
        var comparison = string.IsNullOrWhiteSpace(rule.CompareIndicator)
            ? 0
            : IndicatorCatalog.RequiredBars(rule.CompareIndicator, rule.CompareParams);
        return checked(Math.Max(primary, comparison)
            + Math.Max(rule.WithinBars, rule.ConsecutiveBars));
    }

    private static object PatternConfiguration(PatternType pattern, PatternSettings settings) =>
        pattern switch
        {
            PatternType.GapUpPullback => settings.GapUpPullback,
            PatternType.Breakout => settings.Breakout,
            PatternType.VwapReversion => settings.VwapReversion,
            PatternType.RsiMeanReversion => settings.RsiMeanReversion,
            PatternType.TrendPullback => settings.TrendPullback,
            PatternType.OpeningRangeBreakout => settings.OpeningRangeBreakout,
            PatternType.VolumeSpikeContinuation => settings.VolumeSpikeContinuation,
            PatternType.EarningsDrift => settings.EarningsDrift,
            PatternType.IndexRegimeFilter => settings.IndexRegimeFilter,
            PatternType.VolatilityExpansion => settings.VolatilityExpansion,
            PatternType.MomentumReversal => settings.MomentumReversal,
            PatternType.MultiTimeframeTrend => settings.MultiTimeframeTrend,
            PatternType.MeanReversionChannel => settings.MeanReversionChannel,
            PatternType.Rsi2Bollinger => settings.Rsi2Bollinger,
            PatternType.VolatilityBreakout => settings.VolatilityBreakout,
            PatternType.Tqqq200Sma => settings.Tqqq200Sma,
            PatternType.CumulativeRsi2 => settings.CumulativeRsi2,
            _ => new { }
        };

    private sealed record BuiltInPatternExecutionSnapshot(
        object PatternConfiguration,
        LongPositionExitPolicy ExitPolicy);
}

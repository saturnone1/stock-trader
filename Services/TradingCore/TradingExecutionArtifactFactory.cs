using System.Text.Json;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Optimization.Protocol;
using StockTrader.ServiceContracts.Optimization;
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
            return CreateArtifact(
                TradingExecutionArtifactKinds.StrategyDocument,
                patternCode,
                strategy,
                "{}",
                strategy.CalendarVersion);
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
            MarketCalendarVersion.Current);
    }

    private static TradingStrategyExecutionArtifact CreateArtifact(
        string kind,
        string patternCode,
        StrategyExecutionArtifact? strategy,
        string builtInSettingsJson,
        string calendarVersion)
    {
        var hash = TradingExecutionArtifactPolicy.ComputeDefinitionHash(
            kind, patternCode, strategy, builtInSettingsJson, calendarVersion);
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
            CanManagePosition: true);
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

using Microsoft.Extensions.Options;
using StockTrader.Application.Backtesting;
using StockTrader.Configuration;
using StockTrader.Domain.Backtesting;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

/// <summary>이미 준비된 시세 범위를 슬라이싱하고 공통 시뮬레이션 엔진을 호출합니다.</summary>
public sealed class BacktestPreparedSimulationRunner
{
    private readonly BacktestDataPreparer _dataPreparer;
    private readonly BacktestSimulationEngine _simulation;
    private readonly PatternSettings _basePatternSettings;

    public BacktestPreparedSimulationRunner(
        BacktestDataPreparer dataPreparer,
        BacktestSimulationEngine simulation,
        IOptions<PatternSettings> patternSettings)
    {
        _dataPreparer = dataPreparer;
        _simulation = simulation;
        _basePatternSettings = patternSettings.Value;
    }

    internal async Task<BacktestResult> RunAsync(
        List<string> symbols,
        IReadOnlyDictionary<string, PreparedSymbolData> fullDataMap,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        DateTime from,
        DateTime to,
        decimal initialCapital,
        decimal slippagePercent,
        decimal commissionPerTrade,
        TimeFrame timeFrame,
        BacktestRiskParameters riskParams,
        PatternParameterOverrides? exitOverrides,
        SlippageModel slippageModel,
        WeightStrategy? weightStrategy = null,
        PatternSettings? effectivePatternSettings = null,
        CancellationToken ct = default)
    {
        effectivePatternSettings ??= exitOverrides is null
            ? _basePatternSettings
            : PatternOverrideMerger.Merge(_basePatternSettings, exitOverrides);
        var cumulativeRsi2Config = effectivePatternSettings.CumulativeRsi2;
        var sliceSymbols = symbols
            .Concat(BacktestDetectorMetadata.CollectReferenceSymbols(detectors))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var prepared = _dataPreparer.Slice(
            fullDataMap,
            sliceSymbols,
            timeFrame,
            from,
            to,
            cumulativeRsi2Config,
            effectivePatternSettings.Tqqq200Sma);

        if (!prepared.HasData)
            return new BacktestResult { Warnings = prepared.Warnings.ToList() };

        return await _simulation.RunAsync(
            symbols,
            prepared.Symbols,
            detectors,
            regimeByDate,
            from,
            to,
            initialCapital,
            slippagePercent,
            commissionPerTrade,
            timeFrame,
            riskParams,
            exitOverrides,
            slippageModel,
            prepared.Warnings.ToList(),
            prepared.ActualDataFrom,
            new BacktestExecutionAdapter(),
            weightStrategy,
            cumulativeRsi2Config,
            ct);
    }
}

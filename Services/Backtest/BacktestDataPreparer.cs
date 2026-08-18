using System.Collections.ObjectModel;
using StockTrader.Application.Backtesting;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 백테스트·워크포워드·최적화의 시세 로드와 지표 사전 계산을 한 경로로 통합합니다.
/// </summary>
public sealed class BacktestDataPreparer
{
    private readonly IIndicatorService _indicators;
    private readonly ILogger<BacktestDataPreparer> _logger;

    public BacktestDataPreparer(
        IIndicatorService indicators,
        ILogger<BacktestDataPreparer> logger)
    {
        _indicators = indicators;
        _logger = logger;
    }

    public async Task<PreparedBacktestData> PrepareAsync(
        IDataFeedService dataFeed,
        IEnumerable<string> symbols,
        TimeFrame timeFrame,
        DateTime from,
        DateTime to,
        CumulativeRsi2Config cumulativeRsi2,
        Tqqq200SmaConfig tqqq200Sma,
        CancellationToken ct = default)
    {
        var prepared = new Dictionary<string, PreparedSymbolData>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        DateTime? actualDataFrom = null;
        var warmupCalendarDays = ResolveWarmupCalendarDays(timeFrame, tqqq200Sma);
        var fetchFrom = from.AddDays(-warmupCalendarDays);

        foreach (var symbol in MarketSymbolPolicy.NormalizeMany(symbols))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var bars = await dataFeed.GetHistoricalBarsAsync(symbol, timeFrame, fetchFrom, to, ct);
                if (bars.Count < BacktestDataPolicy.MinimumWarmupBars)
                {
                    var warning = TimeFrameCatalog.IsIntraday(timeFrame)
                        ? $"{symbol}: 분봉 데이터 부족 ({bars.Count}개). 시작일을 조정하세요."
                        : $"{symbol}: 데이터 부족 ({bars.Count}개, 최소 {BacktestDataPolicy.MinimumWarmupBars}개 필요)";
                    warnings.Add(warning);
                    continue;
                }

                var value = Prepare(bars.ToArray(), cumulativeRsi2, tqqq200Sma);
                prepared[symbol] = value;
                var firstTimestamp = value.Bars[0].Timestamp;
                if (!actualDataFrom.HasValue || firstTimestamp < actualDataFrom.Value)
                    actualDataFrom = firstTimestamp;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "{Symbol}/{TimeFrame} 백테스트 데이터 준비 실패", symbol, timeFrame);
                warnings.Add($"{symbol}: 데이터 로드 실패 — {ex.Message}");
            }
        }

        return new PreparedBacktestData(
            new ReadOnlyDictionary<string, PreparedSymbolData>(prepared),
            warnings.AsReadOnly(),
            actualDataFrom);
    }

    public PreparedBacktestData Slice(
        IReadOnlyDictionary<string, PreparedSymbolData> fullData,
        IEnumerable<string> symbols,
        TimeFrame timeFrame,
        DateTime from,
        DateTime to,
        CumulativeRsi2Config cumulativeRsi2,
        Tqqq200SmaConfig tqqq200Sma)
    {
        var prepared = new Dictionary<string, PreparedSymbolData>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        DateTime? actualDataFrom = null;
        var fetchFrom = DateOnly.FromDateTime(
            from.AddDays(-ResolveWarmupCalendarDays(timeFrame, tqqq200Sma)));
        var toDate = DateOnly.FromDateTime(to);

        foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!fullData.TryGetValue(symbol, out var full))
                continue;

            var startIndex = -1;
            var endIndex = -1;
            for (var index = 0; index < full.Bars.Length; index++)
            {
                var date = DateOnly.FromDateTime(full.Bars[index].Timestamp);
                if (date >= fetchFrom && startIndex == -1)
                    startIndex = index;
                if (date <= toDate)
                    endIndex = index;
            }

            if (startIndex == -1 || endIndex < startIndex)
                continue;

            var bars = full.Bars[startIndex..(endIndex + 1)];
            if (bars.Length < BacktestDataPolicy.MinimumWarmupBars)
            {
                warnings.Add($"{symbol}: 데이터 부족 ({bars.Length}개)");
                continue;
            }

            var closes = full.Closes[startIndex..(endIndex + 1)];
            var timestampToIndex = new Dictionary<DateTime, int>(bars.Length);
            for (var index = 0; index < bars.Length; index++)
                timestampToIndex[bars[index].Timestamp] = index;
            var value = new PreparedSymbolData(
                bars,
                full.Atr[startIndex..(endIndex + 1)],
                closes,
                PrepareTqqqProtectiveStopFloors(closes, tqqq200Sma),
                _indicators.CumulativeRsi(
                    closes, cumulativeRsi2.RsiPeriod, cumulativeRsi2.CumulativePeriod),
                _indicators.SMA(closes, cumulativeRsi2.LongTrendMaPeriod),
                timestampToIndex);
            prepared[symbol] = value;
            var firstTimestamp = value.Bars[0].Timestamp;
            if (!actualDataFrom.HasValue || firstTimestamp < actualDataFrom.Value)
                actualDataFrom = firstTimestamp;
        }

        return new PreparedBacktestData(
            new ReadOnlyDictionary<string, PreparedSymbolData>(prepared),
            warnings.AsReadOnly(),
            actualDataFrom);
    }

    private PreparedSymbolData Prepare(
        OhlcvBar[] bars,
        CumulativeRsi2Config cumulativeRsi2,
        Tqqq200SmaConfig tqqq200Sma)
    {
        var closes = IndicatorService.ExtractCloses(bars);
        var timestampToIndex = new Dictionary<DateTime, int>(bars.Length);
        for (var index = 0; index < bars.Length; index++)
            timestampToIndex[bars[index].Timestamp] = index;

        return new PreparedSymbolData(
            bars,
            _indicators.ATR(bars, StrategyEvaluationPolicy.EntryAtrPeriod),
            closes,
            PrepareTqqqProtectiveStopFloors(closes, tqqq200Sma),
            _indicators.CumulativeRsi(closes, cumulativeRsi2.RsiPeriod, cumulativeRsi2.CumulativePeriod),
            _indicators.SMA(closes, cumulativeRsi2.LongTrendMaPeriod),
            timestampToIndex);
    }

    private static int ResolveWarmupCalendarDays(TimeFrame timeFrame, Tqqq200SmaConfig tqqq200Sma)
    {
        var configured = BacktestTimeFramePolicy.Get(timeFrame).WarmupCalendarDays;
        if (timeFrame != TimeFrame.Daily)
            return configured;

        return Math.Max(
            configured,
            Tqqq200SmaExecutionPolicy.RequiredCalendarLookbackDays(tqqq200Sma.SmaPeriod));
    }

    private decimal[] PrepareTqqqProtectiveStopFloors(
        decimal[] closes,
        Tqqq200SmaConfig config)
    {
        if (!Tqqq200SmaExecutionPolicy.IsValidTrendStopConfiguration(
                config.SmaPeriod, config.SmaStopMultiplier))
            return new decimal[closes.Length];

        return _indicators.SMA(closes, config.SmaPeriod)
            .Select(value => Tqqq200SmaExecutionPolicy.ResolveProtectiveStopFloor(
                value, config.SmaStopMultiplier) ?? 0m)
            .ToArray();
    }
}

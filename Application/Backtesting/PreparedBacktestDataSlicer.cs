using System.Collections.ObjectModel;
using StockTrader.Application.Execution;
using StockTrader.Application.MarketData;
using StockTrader.Configuration;
using StockTrader.Engine.Indicators;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.Backtesting;

/// <summary>이미 준비된 전체 배열에서 동일한 워밍업·지표 의미로 실행 구간을 만듭니다.</summary>
public sealed class PreparedBacktestDataSlicer
{
    private static readonly IndicatorCalculator Indicators = new();

    public PreparedBacktestData Slice(
        IReadOnlyDictionary<string, PreparedSymbolData> fullData,
        IEnumerable<string> symbols,
        TimeFrame timeFrame,
        DateTime from,
        DateTime to,
        CumulativeRsi2Config cumulativeRsi2,
        Tqqq200SmaConfig tqqq200Sma,
        MarketDataEvidence evidence)
    {
        var prepared = new Dictionary<string, PreparedSymbolData>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        DateTime? actualDataFrom = null;
        var fetchFrom = DateOnly.FromDateTime(
            from.AddDays(-ResolveWarmupCalendarDays(timeFrame, tqqq200Sma)));
        var toDate = DateOnly.FromDateTime(to);

        foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!fullData.TryGetValue(symbol, out var full)) continue;
            var startIndex = Array.FindIndex(
                full.Bars, bar => DateOnly.FromDateTime(bar.Timestamp) >= fetchFrom);
            var endIndex = Array.FindLastIndex(
                full.Bars, bar => DateOnly.FromDateTime(bar.Timestamp) <= toDate);
            if (startIndex == -1 || endIndex < startIndex) continue;

            var bars = full.Bars[startIndex..(endIndex + 1)];
            if (bars.Length < BacktestDataPolicy.MinimumWarmupBars)
            {
                warnings.Add($"{symbol}: 데이터 부족 ({bars.Length}개)");
                continue;
            }

            var closes = full.Closes[startIndex..(endIndex + 1)];
            var timestampToIndex = bars
                .Select((bar, index) => (bar.Timestamp, index))
                .ToDictionary(item => item.Timestamp, item => item.index);
            var value = new PreparedSymbolData(
                bars,
                full.Atr[startIndex..(endIndex + 1)],
                closes,
                PrepareTqqqProtectiveStopFloors(closes, tqqq200Sma),
                Indicators.CumulativeRsi(
                    closes, cumulativeRsi2.RsiPeriod, cumulativeRsi2.CumulativePeriod),
                Indicators.SMA(closes, cumulativeRsi2.LongTrendMaPeriod),
                timestampToIndex);
            prepared[symbol] = value;
            if (!actualDataFrom.HasValue || value.Bars[0].Timestamp < actualDataFrom)
                actualDataFrom = value.Bars[0].Timestamp;
        }

        return new PreparedBacktestData(
            new ReadOnlyDictionary<string, PreparedSymbolData>(prepared),
            warnings.AsReadOnly(),
            actualDataFrom,
            evidence);
    }

    private static int ResolveWarmupCalendarDays(TimeFrame timeFrame, Tqqq200SmaConfig config)
    {
        var configured = BacktestTimeFramePolicy.Get(timeFrame).WarmupCalendarDays;
        return timeFrame == TimeFrame.Daily
            ? Math.Max(configured,
                Tqqq200SmaExecutionPolicy.RequiredCalendarLookbackDays(config.SmaPeriod))
            : configured;
    }

    private static decimal[] PrepareTqqqProtectiveStopFloors(
        decimal[] closes,
        Tqqq200SmaConfig config)
    {
        if (!Tqqq200SmaExecutionPolicy.IsValidTrendStopConfiguration(
                config.SmaPeriod, config.SmaStopMultiplier))
            return new decimal[closes.Length];
        return Indicators.SMA(closes, config.SmaPeriod)
            .Select(value => Tqqq200SmaExecutionPolicy.ResolveProtectiveStopFloor(
                value, config.SmaStopMultiplier) ?? 0m)
            .ToArray();
    }
}

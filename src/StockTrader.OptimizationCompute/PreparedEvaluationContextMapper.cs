using System.Text.Json;
using StockTrader.Application.Backtesting;
using StockTrader.Application.MarketData;
using StockTrader.Application.Optimization;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Optimization.Compute;

internal static class PreparedEvaluationContextMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static OptimizationEvaluationContext Map(
        OptimizeRequest request,
        OptimizationEvaluationInput input)
    {
        var settings = JsonSerializer.Deserialize<PatternSettings>(
            input.PreparedData.PatternSettingsJson, Json) ?? new PatternSettings();
        var data = input.PreparedData.Series
            .GroupBy(series => Parse<TimeFrame>(series.TimeFrame))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, PreparedSymbolData>)group.ToDictionary(
                    series => series.Symbol,
                    MapSeries,
                    StringComparer.OrdinalIgnoreCase));
        var evidence = input.DataEvidence.Series
            .GroupBy(item => Parse<TimeFrame>(item.TimeFrame))
            .ToDictionary(group => group.Key, group => MapEvidence(group.First()));
        var defaultFrame = data.ContainsKey(request.TimeFrame)
            ? request.TimeFrame
            : data.Keys.OrderBy(frame => frame).First();
        var regimes = input.PreparedData.Regimes.ToDictionary(
            item => item.Date,
            item => new MarketRegime
            {
                SpyAbove200Ma = item.BenchmarkAboveLongAverage,
                SpyPrice = item.BenchmarkPrice,
                Spy200Ma = item.BenchmarkLongAverage,
                VixLevel = item.VolatilityLevel,
                RegimeLabel = item.Label,
                AsOf = item.AsOf,
                MlClusterId = item.MlClusterId,
                MlRegimeLabel = item.MlLabel
            });
        var risk = input.PreparedData.Risk;
        return new OptimizationEvaluationContext(
            request,
            data,
            data[defaultFrame],
            regimes,
            new OptimizationRiskParameters(
                risk.RiskPerTradePercent,
                risk.DailyLossLimitPercent,
                risk.MaxTotalPositions,
                risk.MaxPositionsPerSector),
            evidence,
            evidence[defaultFrame])
        {
            PatternSettings = settings
        };
    }

    private static PreparedSymbolData MapSeries(OptimizationPreparedSeries series)
    {
        var frame = Parse<TimeFrame>(series.TimeFrame);
        var bars = series.Bars.Select(bar => new OhlcvBar
        {
            Symbol = series.Symbol,
            TimeFrame = frame,
            Timestamp = bar.Timestamp,
            Open = bar.Open,
            High = bar.High,
            Low = bar.Low,
            Close = bar.Close,
            Volume = bar.Volume,
            Vwap = bar.Vwap
        }).ToArray();
        return new PreparedSymbolData(
            bars,
            series.Atr.ToArray(),
            series.Closes.ToArray(),
            series.TqqqProtectiveStopFloor.ToArray(),
            series.CumulativeRsi2.ToArray(),
            series.CumulativeRsi2TrendMa.ToArray(),
            bars.Select((bar, index) => (bar.Timestamp, index))
                .ToDictionary(item => item.Timestamp, item => item.index));
    }

    private static MarketDataEvidence MapEvidence(OptimizationSymbolDataEvidence evidence)
    {
        var region = Parse<MarketRegion>(evidence.Market);
        return new MarketDataEvidence(
            Parse<DataSource>(evidence.Provider),
            region,
            string.IsNullOrWhiteSpace(evidence.MarketTimeZoneId)
                ? MarketRegionCatalog.Get(region).TimeZoneId
                : evidence.MarketTimeZoneId,
            Parse<TimeFrame>(evidence.TimeFrame),
            Parse<PriceAdjustmentMode>(evidence.AdjustmentMode),
            Parse<MarketSessionScope>(evidence.SessionScope),
            evidence.CalendarVersion,
            evidence.WarmupCalendarDays,
            evidence.RequiredWarmupBars);
    }

    private static T Parse<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unsupported {typeof(T).Name}: {value}");
}

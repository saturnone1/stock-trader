using StockTrader.Application.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Backtest;

/// <summary>벤치마크 일봉에서 과거 시점별 장기 추세 레짐을 준비합니다.</summary>
public sealed class BacktestRegimeMapBuilder
{
    private readonly IIndicatorService _indicators;
    private readonly ILogger<BacktestRegimeMapBuilder> _logger;

    public BacktestRegimeMapBuilder(
        IIndicatorService indicators,
        ILogger<BacktestRegimeMapBuilder> logger)
    {
        _indicators = indicators;
        _logger = logger;
    }

    internal async Task<Dictionary<DateOnly, MarketRegime>?> BuildAsync(
        IDataFeedService dataFeed,
        DateTime from,
        DateTime to,
        string regimeSymbol = "SPY",
        CancellationToken ct = default)
    {
        var lookbackFrom = from.AddDays(
            -StrategyEvaluationPolicy.RegimeLookbackCalendarDays);
        List<OhlcvBar> indexBars;
        try
        {
            indexBars = await dataFeed.GetHistoricalBarsAsync(
                regimeSymbol, TimeFrame.Daily, lookbackFrom, to, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "{Symbol} 데이터 조회 실패", regimeSymbol);
            return null;
        }

        if (indexBars.Count < StrategyEvaluationPolicy.RegimeTrendBars)
        {
            _logger.LogWarning(
                "{Symbol} 데이터 부족: {Count}개 (최소 {Minimum}개 필요), 기본 강세 레짐 적용",
                regimeSymbol,
                indexBars.Count,
                StrategyEvaluationPolicy.RegimeTrendBars);
            var fallback = new Dictionary<DateOnly, MarketRegime>();
            for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
            {
                fallback[DateOnly.FromDateTime(date)] = new MarketRegime
                {
                    SpyAbove200Ma = true,
                    SpyPrice = 0,
                    Spy200Ma = 0,
                    RegimeLabel = "강세(기본)",
                    AsOf = date
                };
            }
            return fallback;
        }

        var bars = indexBars.OrderBy(bar => bar.Timestamp).ToArray();
        var closes = IndicatorService.ExtractCloses(bars);
        var trend = _indicators.SMA(
            closes, StrategyEvaluationPolicy.RegimeTrendBars);
        var result = new Dictionary<DateOnly, MarketRegime>();
        for (var index = 0; index < bars.Length; index++)
        {
            var aboveTrend = trend[index] > 0 && bars[index].Close > trend[index];
            result[DateOnly.FromDateTime(bars[index].Timestamp)] = new MarketRegime
            {
                SpyAbove200Ma = aboveTrend,
                SpyPrice = bars[index].Close,
                Spy200Ma = trend[index],
                RegimeLabel = aboveTrend ? "강세" : "약세",
                AsOf = bars[index].Timestamp
            };
        }
        return result;
    }
}

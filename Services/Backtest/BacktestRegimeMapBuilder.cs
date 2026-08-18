using StockTrader.Application.Strategies;
using StockTrader.Application.Backtesting;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;

namespace StockTrader.Services.Backtest;

/// <summary>벤치마크 일봉에서 과거 시점별 장기 추세 레짐을 준비합니다.</summary>
public sealed class BacktestRegimeMapBuilder
{
    private readonly ILogger<BacktestRegimeMapBuilder> _logger;

    public BacktestRegimeMapBuilder(ILogger<BacktestRegimeMapBuilder> logger)
    {
        _logger = logger;
    }

    internal async Task<Dictionary<DateOnly, MarketRegime>?> BuildAsync(
        IDataFeedService dataFeed,
        DateTime from,
        DateTime to,
        string regimeSymbol = DataProviderCatalog.UnitedStatesRegimeBenchmark,
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
                "{Symbol} 데이터 부족: {Count}개 (최소 {Minimum}개 필요), 알 수 없음 레짐 적용",
                regimeSymbol,
                indexBars.Count,
                StrategyEvaluationPolicy.RegimeTrendBars);
        }

        var bars = indexBars.OrderBy(bar => bar.Timestamp).ToArray();
        var result = new Dictionary<DateOnly, MarketRegime>();
        for (var index = 0; index < bars.Length; index++)
        {
            var timestamp = bars[index].Timestamp;
            result[DateOnly.FromDateTime(timestamp)] =
                MarketRegimeTrendPolicy.Evaluate(bars[..(index + 1)], timestamp);
        }

        if (result.Count == 0)
            result[DateOnly.FromDateTime(from)] = MarketRegimeTrendPolicy.Unknown(from);

        return result;
    }
}

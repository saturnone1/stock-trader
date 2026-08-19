using StockTrader.Application.MarketData;
using StockTrader.Models;

namespace StockTrader.Application.Backtesting;

/// <summary>백테스트·최적화가 공유하는 심볼별 시세와 사전 계산 지표입니다.</summary>
public sealed record PreparedSymbolData(
    OhlcvBar[] Bars,
    decimal[] Atr,
    decimal[] Closes,
    decimal[] TqqqProtectiveStopFloor,
    decimal[] CumulativeRsi2,
    decimal[] CumulativeRsi2TrendMa,
    Dictionary<DateTime, int> TimestampToIndex);

/// <summary>데이터 준비 결과와 데이터 품질 경고를 함께 전달합니다.</summary>
public sealed record PreparedBacktestData(
    IReadOnlyDictionary<string, PreparedSymbolData> Symbols,
    IReadOnlyList<string> Warnings,
    DateTime? ActualDataFrom,
    MarketDataEvidence Evidence)
{
    public bool HasData => Symbols.Count > 0;
}

public static class BacktestDataPolicy
{
    /// <summary>지표 워밍업과 체결 시뮬레이션에 필요한 최소 봉 수입니다.</summary>
    public const int MinimumWarmupBars = Strategies.StrategyEvaluationPolicy.MinimumWarmupBars;
}

public static class BacktestTimeline
{
    public static List<DateTime> Build(IEnumerable<PreparedSymbolData> preparedData, DateTime from) =>
        preparedData
            .SelectMany(data => data.TimestampToIndex.Keys)
            .Distinct()
            .Where(timestamp => timestamp >= from)
            .OrderBy(timestamp => timestamp)
            .ToList();
}

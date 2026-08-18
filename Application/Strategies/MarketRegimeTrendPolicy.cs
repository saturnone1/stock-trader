using StockTrader.Models;

namespace StockTrader.Application.Strategies;

/// <summary>
/// 완료된 기준 종목 일봉만 사용해 장기 추세 국면을 결정합니다.
/// 준비봉이 부족하면 강세를 추정하지 않고 알 수 없음으로 실패 폐쇄합니다.
/// </summary>
public static class MarketRegimeTrendPolicy
{
    public const string BullishLabel = "강세";
    public const string BearishLabel = "약세";
    public const string UnknownLabel = "알 수 없음";
    public const string InsufficientHistoryWarning =
        "기준 종목의 완료 일봉이 200개보다 적은 구간은 시장 국면을 알 수 없음으로 처리해 "
        + "강세장 전용 진입을 제한하고 약세장 비중을 적용합니다.";

    public static MarketRegime Evaluate(
        IReadOnlyList<OhlcvBar> benchmarkBars,
        DateTime asOf)
    {
        ArgumentNullException.ThrowIfNull(benchmarkBars);

        var available = benchmarkBars
            .Where(bar => bar.Timestamp <= asOf)
            .OrderBy(bar => bar.Timestamp)
            .TakeLast(StrategyEvaluationPolicy.RegimeTrendBars)
            .ToArray();
        var latest = available.LastOrDefault();

        if (available.Length < StrategyEvaluationPolicy.RegimeTrendBars)
            return Unknown(asOf, latest);

        var movingAverage = available.Average(bar => bar.Close);
        var price = latest!.Close;
        var isBullish = movingAverage > 0 && price > movingAverage;
        return new MarketRegime
        {
            SpyAbove200Ma = isBullish,
            SpyPrice = price,
            Spy200Ma = movingAverage,
            RegimeLabel = isBullish ? BullishLabel : BearishLabel,
            AsOf = latest.Timestamp
        };
    }

    public static MarketRegime Unknown(DateTime asOf, OhlcvBar? latest = null) => new()
    {
        SpyAbove200Ma = false,
        SpyPrice = latest?.Close ?? 0,
        Spy200Ma = 0,
        RegimeLabel = UnknownLabel,
        AsOf = latest?.Timestamp ?? asOf
    };

    public static bool IsUnknown(MarketRegime regime) =>
        string.Equals(regime.RegimeLabel, UnknownLabel, StringComparison.Ordinal);
}

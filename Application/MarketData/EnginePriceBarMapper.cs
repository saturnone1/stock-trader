using StockTrader.Engine.MarketData;
using StockTrader.Models;

namespace StockTrader.Application.MarketData;

/// <summary>영속·공급자 가격봉을 결정론 엔진 입력으로 투영하는 단일 경계입니다.</summary>
public static class EnginePriceBarMapper
{
    public static PriceBar Map(OhlcvBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        return new(
            bar.Timestamp,
            bar.TimeFrame,
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            bar.Volume,
            bar.Vwap);
    }

    public static PriceBar[] Map(IReadOnlyList<OhlcvBar> bars)
    {
        var result = new PriceBar[bars.Count];
        for (var i = 0; i < bars.Count; i++) result[i] = Map(bars[i]);
        return result;
    }
}

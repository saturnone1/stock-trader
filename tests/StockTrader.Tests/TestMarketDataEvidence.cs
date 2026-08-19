using StockTrader.Application.MarketData;
using StockTrader.Domain.MarketData;

namespace StockTrader.Tests;

/// <summary>
/// 골든/패리티 테스트가 공유하는 데이터 근거. 시뮬레이션 결과에 영향을 주지 않으며,
/// 결과가 근거를 그대로 보존하는지 확인하는 데 사용한다.
/// </summary>
internal static class TestMarketDataEvidence
{
    public static MarketDataEvidence Daily { get; } = MarketDataEvidence.Create(
        DataSource.Alpaca,
        TimeFrame.Daily,
        MarketSessionScope.RegularSessionOnly,
        warmupCalendarDays: 400,
        requiredWarmupBars: 50);
}

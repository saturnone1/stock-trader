using StockTrader.Models.Enums;

namespace StockTrader.Application.Backtesting;

/// <summary>데이터 시장에 맞는 장기 추세 기준 종목을 선택합니다.</summary>
public static class MarketRegimeBenchmarkPolicy
{
    public const string UnitedStatesBenchmark = "SPY";
    public const string KoreaBenchmark = "069500";

    public static string Resolve(DataSource dataSource) =>
        dataSource == DataSource.LsSecurities
            ? KoreaBenchmark
            : UnitedStatesBenchmark;
}

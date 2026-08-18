namespace StockTrader.Domain.Strategies;

/// <summary>종목별 전략 배정의 안정적인 기본값과 입력 한계입니다.</summary>
public static class SymbolProfilePolicy
{
    public const string DefaultName = "기본";
    public const int MaximumNameLength = 80;
    public const decimal DefaultRiskPerTradePercent = 0.01m;
    public const int DefaultMaximumPositions = 7;
}

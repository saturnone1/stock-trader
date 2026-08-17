namespace StockTrader.Domain.Strategies;

/// <summary>새 저장 전략과 생략된 API 필드가 공유하는 문서 기본값.</summary>
public static class StrategyDocumentDefaults
{
    public const string EmptyListJson = "[]";
    public const string EmptyObjectJson = "{}";
    public const string AndLogic = "AND";
    public const string OrLogic = "OR";
    public const decimal AtrStopMultiplier = 2m;
    public const decimal AtrTargetMultiplier = 3m;
    public const int MaxHoldingBars = 10;
    public const decimal DefaultAllocationPercent = 100m;
    public const bool IsActive = true;
}

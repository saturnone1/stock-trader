namespace StockTrader.Models.Enums;

/// <summary>보유 중인 롱 포지션에 적용할 브로커 체결의 의미.</summary>
public enum PositionExecutionKind
{
    FullExit = 0,
    PartialProfit = 1,
    ScaleIn = 2,
    ScaleOut = 3,
}

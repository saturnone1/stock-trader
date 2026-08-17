namespace StockTrader.Services.Backtest;

/// <summary>백테스트 실행 시 사용하는 포트폴리오 리스크 한도입니다.</summary>
internal sealed record BacktestRiskParameters(
    decimal RiskPerTradePercent,
    decimal DailyLossLimitPercent,
    int MaxTotalPositions,
    int MaxPositionsPerSector);

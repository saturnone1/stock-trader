namespace StockTrader.Models;

public class StockAnalysis
{
    public string Symbol { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public DateTime AnalyzedAt { get; set; }

    // 핵심 예측 수치
    public decimal UpsideProbability { get; set; }      // 상승 확률 (0~100%)
    public decimal ExpectedReturnPercent { get; set; }   // 예상 수익률 %
    public int ExpectedHoldingDays { get; set; }         // 예상 보유 기간 (일)
    public decimal DownsideRiskPercent { get; set; }     // 손실 위험 %
    public decimal RecommendedStopLoss { get; set; }     // 추천 손절가
    public decimal RecommendedTarget { get; set; }       // 추천 목표가
    public decimal ConfidenceScore { get; set; }         // 종합 신뢰도 (0~100)

    // 추천 등급
    public RecommendationGrade Grade { get; set; }

    // 세부 분석
    public List<PatternSignalInfo> ActivePatterns { get; set; } = new();
    public IndicatorSnapshot Indicators { get; set; } = new();
    public decimal ATR { get; set; }
}

public enum RecommendationGrade
{
    StrongBuy,    // 강력 매수
    Buy,          // 매수
    Neutral,      // 중립
    Sell,         // 매도
    StrongSell    // 강력 매도
}

public class PatternSignalInfo
{
    public PatternType PatternType { get; set; }
    public decimal Confidence { get; set; }
    public decimal HistoricalWinRate { get; set; }
    public decimal HistoricalAvgReturn { get; set; }
}

public class IndicatorSnapshot
{
    public decimal RSI { get; set; }
    public decimal SMA20 { get; set; }
    public decimal SMA50 { get; set; }
    public decimal SMA200 { get; set; }
    public decimal MACD { get; set; }
    public decimal MACDSignal { get; set; }
    public decimal BollingerUpper { get; set; }
    public decimal BollingerMiddle { get; set; }
    public decimal BollingerLower { get; set; }
    public decimal VWAP { get; set; }
    public int BullishIndicatorCount { get; set; }
    public int TotalIndicatorCount { get; set; }
}

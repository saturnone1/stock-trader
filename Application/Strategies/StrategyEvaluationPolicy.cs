namespace StockTrader.Application.Strategies;

/// <summary>모든 전략 실행 경로가 공유하는 데이터 평가 전제입니다.</summary>
public static class StrategyEvaluationPolicy
{
    /// <summary>지표 워밍업과 첫 신호 평가 전에 확보해야 하는 최소 봉 수입니다.</summary>
    public const int MinimumWarmupBars = 50;

    /// <summary>시장 장기 추세 레짐을 판정하는 이동평균 기간입니다.</summary>
    public const int RegimeTrendBars = 200;

    /// <summary>200거래일 레짐을 안정적으로 준비하기 위한 기본 달력일 조회 범위입니다.</summary>
    public const int RegimeLookbackCalendarDays = 400;

    /// <summary>실시간 일봉 신호가 지표 워밍업을 포함해 조회하는 기본 달력일 범위입니다.</summary>
    public const int LiveDailySignalLookbackDays = 365;

    /// <summary>진입 위험과 체결 비용에 사용하는 기본 ATR 기간입니다.</summary>
    public const int EntryAtrPeriod = 14;

    /// <summary>실시간 청산 지표와 참조 종목을 준비하는 기본 달력일 범위입니다.</summary>
    public const int LivePositionIndicatorLookbackDays = 400;

    /// <summary>ATR과 저장 위험거리가 모두 없을 때 사용하는 최후의 가격 위험 비율입니다.</summary>
    public const decimal FallbackRiskFraction = 0.02m;
}

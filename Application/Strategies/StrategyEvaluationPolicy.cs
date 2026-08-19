namespace StockTrader.Application.Strategies;

/// <summary>모든 전략 실행 경로가 공유하는 데이터 평가 전제입니다.</summary>
public static class StrategyEvaluationPolicy
{
    /// <summary>지표 워밍업과 첫 신호 평가 전에 확보해야 하는 최소 봉 수입니다.</summary>
    public const int MinimumWarmupBars = 50;

    /// <summary>
    /// 전략을 평가할 수 있는 첫 봉의 인덱스.
    ///
    /// 인덱스 <c>i</c> 인 봉을 평가할 때 확보된 이력은 <c>bars[0..i]</c>, 즉 <c>i + 1</c> 개다.
    /// 따라서 <see cref="MinimumWarmupBars"/> 개를 요구한다는 것은 인덱스
    /// <c>MinimumWarmupBars - 1</c> 이 첫 평가 가능 봉이라는 뜻이다. 이 값을 엔진마다
    /// 따로 계산하면 같은 전략이 미리보기와 백테스트에서 서로 다른 봉부터 시작해,
    /// 한쪽에만 존재하는 진입이 생기고 그 진입이 이후 쿨다운 일정 전체를 밀어낸다.
    /// </summary>
    public const int FirstEvaluableBarIndex = MinimumWarmupBars - 1;

    /// <summary>일봉 기본 패턴 스캐너가 평가를 시작할 최소 봉 수입니다.</summary>
    public const int LiveScannerMinimumBars = 20;

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

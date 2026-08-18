export const dayOptions = [
  { value: 1, label: '월' }, { value: 2, label: '화' }, { value: 3, label: '수' },
  { value: 4, label: '목' }, { value: 5, label: '금' }, { value: 6, label: '토' }, { value: 0, label: '일' }
]

export const monthOptions = Array.from({ length: 12 }, (_, index) => index + 1)

export const operatorLabels = {
  '>': '초과',
  '<': '미만',
  '>=': '이상',
  '<=': '이하',
  crosses_above: '상향 돌파',
  crosses_below: '하향 이탈'
}

export const paramKeyLabels = {
  period: '기간',
  cumulativePeriod: '누적 기간',
  bars: '봉 수',
  lookback: '되돌아보기',
  stddev: '표준편차',
  percent: '퍼센트',
  multiple: 'R 배수',
  multiplier: '배수',
  smooth: '평활',
  slow: '느린 기간',
  fast: '빠른 기간',
  signal: '시그널 기간'
}

export const glossaryTooltips = {
  workspace: '저장한 매매 전략을 고르고 새 전략을 만드는 곳입니다.',
  pattern: '한 전략에서 언제 사고, 얼마나 사고, 언제 팔지 정하는 기본 설정입니다.',
  strategy: '매수 조건부터 손절·익절과 거래 제한까지 실제 매매 순서대로 구성합니다.',
  rule: 'RSI가 30 이하인지, 거래량이 평균보다 큰지처럼 매수·매도를 판단하는 한 가지 조건입니다.',
  entryGroup: '같이 확인할 매수 조건을 한 상황으로 묶습니다. 모든 조건 또는 하나 이상의 조건을 만족하도록 정할 수 있습니다.',
  exitRule: '보유한 종목을 언제 팔지 정하는 조건입니다.',
  weightTier: '시장 상황이나 조건에 따라 투자 비중을 다르게 정합니다.',
  scalingRule: '보유 중 추가로 사거나 일부를 팔 시점과 수량을 정합니다.',
  runtime: '거래 가능한 시기, 손실 후 휴식, 동시 보유 한도처럼 전략 전체의 안전장치를 정합니다.',
  dynamicExit: 'ATR, 이동평균, 이전 고점·저점 등을 이용해 손절가와 목표가를 계산합니다.',
  ruleInspector: '선택한 매수·매도 조건의 지표와 기준값을 바꾸는 곳입니다.',
  entryMode: '신호가 뜬 현재 봉 종가에 바로 들어갈지, 다음 봉 시가에 들어갈지 정합니다.',
  sizingMode: '주문 크기를 어떤 방식으로 계산할지 정합니다.'
}

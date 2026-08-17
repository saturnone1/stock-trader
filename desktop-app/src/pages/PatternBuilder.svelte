<script>
  import { onMount } from 'svelte'
  import { ArrowDown, ArrowUp, ChevronRight, CircleHelp, Copy, FolderTree, Plus, Save, Trash2 } from 'lucide-svelte'
  import { patternApi } from '../api/endpoints'
  import PatternPreview from '../lib/PatternPreview.svelte'

  const indicatorPalette = [
    {
      title: '가격 구조',
      items: [
        { label: '돌파 고점', indicator: 'BREAKOUT_HIGH', operator: '>=', value: 1, params: { period: 20 } },
        { label: '돌파 저점', indicator: 'BREAKOUT_LOW', operator: '>=', value: 1, params: { period: 20 } },
        { label: '고점 대비 거리', indicator: 'DIST_FROM_HIGH', operator: '<=', value: 2, params: { period: 20 } },
        { label: '저점 대비 거리', indicator: 'DIST_FROM_LOW', operator: '>=', value: 5, params: { period: 20 } },
        { label: '갭 상승', indicator: 'GAP', operator: '>=', value: 1.5, params: {} },
        { label: '저점 상승 지속', indicator: 'HIGHER_LOW', operator: '>=', value: 2, params: {} },
        { label: '고점 하락 지속', indicator: 'LOWER_HIGH', operator: '>=', value: 2, params: {} },
        { label: '인사이드 바', indicator: 'INSIDE_BAR', operator: '>=', value: 1, params: {} },
        { label: '장악형 캔들', indicator: 'ENGULFING', operator: '>=', value: 1, params: {} },
      ]
    },
    {
      title: '모멘텀',
      items: [
        { label: 'RSI 과매도', indicator: 'RSI', operator: '<=', value: 30, params: { period: 14 } },
        { label: '누적 RSI', indicator: 'CUMULATIVE_RSI', operator: '<=', value: 10, params: { period: 2, cumulativePeriod: 2 } },
        { label: '스토캐스틱 K', indicator: 'STOCHASTIC_K', operator: '<=', value: 20, params: { period: 14 } },
        { label: '스토캐스틱 D', indicator: 'STOCHASTIC_D', operator: '<=', value: 20, params: { period: 14, smooth: 3 } },
        { label: 'MACD 히스토그램', indicator: 'MACD_HIST', operator: '>', value: 0, params: {} },
        { label: '연속 상승', indicator: 'CONSECUTIVE_UP', operator: '>=', value: 3, params: {} },
        { label: '연속 하락', indicator: 'CONSECUTIVE_DOWN', operator: '>=', value: 3, params: {} },
        { label: 'ADX 추세', indicator: 'ADX', operator: '>=', value: 25, params: { period: 14 } },
        { label: 'ROC 모멘텀', indicator: 'ROC', operator: '>=', value: 5, params: { period: 14 } },
        { label: 'CCI', indicator: 'CCI', operator: '<=', value: -100, params: { period: 20 } },
        { label: '윌리엄스 %R', indicator: 'WILLIAMS_R', operator: '<=', value: -80, params: { period: 14 } },
      ]
    },
    {
      title: '추세/평균',
      items: [
        { label: '가격 vs SMA', indicator: 'PRICE_VS_SMA', operator: '>', value: 0, params: { period: 20 } },
        { label: '가격 vs EMA', indicator: 'PRICE_VS_EMA', operator: '>', value: 0, params: { period: 20 } },
        { label: '가격 vs VWAP', indicator: 'PRICE_VS_VWAP', operator: '>', value: 0, params: { period: 20 } },
        { label: 'SMA 기울기', indicator: 'SMA_SLOPE', operator: '>', value: 0, params: { period: 20, lookback: 5 } },
        { label: 'OBV 누적거래량', indicator: 'OBV', operator: '>', value: 0, params: {} },
        { label: 'OBV 기울기', indicator: 'OBV_SLOPE', operator: '>', value: 0, params: { lookback: 5 } },
        { label: '거래량 비율', indicator: 'VOLUME_RATIO', operator: '>=', value: 1.5, params: { period: 20 } },
        { label: 'CMF', indicator: 'CMF', operator: '>', value: 0, params: { period: 20 } },
      ]
    },
    {
      title: '변동성/기타',
      items: [
        { label: '볼린저 위치', indicator: 'BOLLINGER_POS', operator: '<=', value: 0.1, params: { period: 20, stddev: 2 } },
        { label: 'ATR', indicator: 'ATR', operator: '>=', value: 1, params: { period: 14 } },
        { label: 'ATR %', indicator: 'ATR_PERCENT', operator: '>=', value: 2, params: { period: 14 } },
        { label: '가격 변화율', indicator: 'PRICE_CHANGE', operator: '>=', value: 3, params: { bars: 5 } },
        { label: '20일 변동성', indicator: 'VOLATILITY_20D', operator: '>=', value: 30, params: { period: 20 } },
        { label: '캔들 바디', indicator: 'CANDLE_BODY', operator: '>=', value: 1, params: {} },
      ]
    }
  ]

  const operatorOptions = ['>', '<', '>=', '<=', 'crosses_above', 'crosses_below']
  const entryModeOptions = ['CurrentClose', 'NextOpen']
  const sizingModeOptions = ['FixedRisk', 'Kelly', 'HalfKelly']
  const logicOptions = ['AND', 'OR']
  const scalingDirectionOptions = ['SCALE_IN', 'SCALE_OUT']
  const stopTypeOptions = ['ATR', 'BOLLINGER_LOWER', 'SMA', 'EMA', 'PREV_LOW', 'PERCENT']
  const targetTypeOptions = ['ATR', 'BOLLINGER_UPPER', 'SMA', 'EMA', 'PREV_HIGH', 'R_MULTIPLE', 'PERCENT']
  const indicatorOptions = indicatorPalette.flatMap((section) => section.items)
  const indicatorSet = new Set(indicatorOptions.map((item) => item.indicator))
  const indicatorLabels = Object.fromEntries(indicatorOptions.map((item) => [item.indicator, item.label]))
  const operatorLabels = {
    '>': '초과',
    '<': '미만',
    '>=': '이상',
    '<=': '이하',
    crosses_above: '상향 돌파',
    crosses_below: '하향 이탈'
  }
  const entryModeLabels = {
    CurrentClose: '신호 봉의 종가에 매수',
    NextOpen: '다음 봉의 시가에 매수'
  }
  const sizingModeLabels = {
    FixedRisk: '손실 허용액 기준',
    Kelly: '켈리 공식',
    HalfKelly: '절반 켈리 공식'
  }
  const logicLabels = {
    AND: '모두 만족',
    OR: '하나만 만족'
  }
  const scalingDirectionLabels = {
    SCALE_IN: '추가 매수',
    SCALE_OUT: '일부 매도'
  }
  const stopTypeLabels = {
    ATR: 'ATR 기준',
    BOLLINGER_LOWER: '볼린저 하단',
    SMA: '단순이동평균',
    EMA: '지수이동평균',
    PREV_LOW: '이전 저점',
    PERCENT: '퍼센트 기준'
  }
  const targetTypeLabels = {
    ATR: 'ATR 기준',
    BOLLINGER_UPPER: '볼린저 상단',
    SMA: '단순이동평균',
    EMA: '지수이동평균',
    PREV_HIGH: '이전 고점',
    R_MULTIPLE: 'R 배수',
    PERCENT: '퍼센트 기준'
  }
  const paramKeyLabels = {
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
  const glossaryTooltips = {
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
  const dynamicExitFieldConfigs = {
    stop: {
      ATR: [
        { key: 'multiplier', label: 'ATR 배수', step: '0.1', defaultValue: 2 },
        { key: 'period', label: 'ATR 기간', step: '1', defaultValue: 14 }
      ],
      BOLLINGER_LOWER: [
        { key: 'period', label: '기간', step: '1', defaultValue: 20 },
        { key: 'stddev', label: '표준편차', step: '0.1', defaultValue: 2 }
      ],
      SMA: [{ key: 'period', label: '기간', step: '1', defaultValue: 20 }],
      EMA: [{ key: 'period', label: '기간', step: '1', defaultValue: 20 }],
      PREV_LOW: [{ key: 'period', label: '되돌아보기', step: '1', defaultValue: 5 }],
      PERCENT: [{ key: 'percent', label: '퍼센트', step: '0.1', defaultValue: 2 }]
    },
    target: {
      ATR: [
        { key: 'multiplier', label: 'ATR 배수', step: '0.1', defaultValue: 3 },
        { key: 'period', label: 'ATR 기간', step: '1', defaultValue: 14 }
      ],
      BOLLINGER_UPPER: [
        { key: 'period', label: '기간', step: '1', defaultValue: 20 },
        { key: 'stddev', label: '표준편차', step: '0.1', defaultValue: 2 }
      ],
      SMA: [{ key: 'period', label: '기간', step: '1', defaultValue: 20 }],
      EMA: [{ key: 'period', label: '기간', step: '1', defaultValue: 20 }],
      PREV_HIGH: [{ key: 'period', label: '되돌아보기', step: '1', defaultValue: 5 }],
      R_MULTIPLE: [{ key: 'multiple', label: 'R 배수', step: '0.1', defaultValue: 3 }],
      PERCENT: [{ key: 'percent', label: '퍼센트', step: '0.1', defaultValue: 5 }]
    }
  }
  const indicatorFieldConfigs = {
    BREAKOUT_HIGH: [{ key: 'period', label: '돌파 기준 기간', step: '1', defaultValue: 20 }],
    BREAKOUT_LOW: [{ key: 'period', label: '돌파 기준 기간', step: '1', defaultValue: 20 }],
    DIST_FROM_HIGH: [{ key: 'period', label: '기준 고점 기간', step: '1', defaultValue: 20 }],
    DIST_FROM_LOW: [{ key: 'period', label: '기준 저점 기간', step: '1', defaultValue: 20 }],
    GAP: [],
    HIGHER_LOW: [],
    LOWER_HIGH: [],
    INSIDE_BAR: [],
    ENGULFING: [],
    RSI: [{ key: 'period', label: 'RSI 기간', step: '1', defaultValue: 14 }],
    CUMULATIVE_RSI: [
      { key: 'period', label: 'RSI 기간', step: '1', defaultValue: 2 },
      { key: 'cumulativePeriod', label: '누적 기간', step: '1', defaultValue: 2 }
    ],
    STOCHASTIC_K: [{ key: 'period', label: '스토캐스틱 기간', step: '1', defaultValue: 14 }],
    STOCHASTIC_D: [
      { key: 'period', label: '스토캐스틱 기간', step: '1', defaultValue: 14 },
      { key: 'smooth', label: '평활 기간', step: '1', defaultValue: 3 }
    ],
    MACD_HIST: [
      { key: 'fast', label: '빠른 EMA', step: '1', defaultValue: 12 },
      { key: 'slow', label: '느린 EMA', step: '1', defaultValue: 26 },
      { key: 'signal', label: '시그널 기간', step: '1', defaultValue: 9 }
    ],
    CONSECUTIVE_UP: [],
    CONSECUTIVE_DOWN: [],
    ADX: [{ key: 'period', label: 'ADX 기간', step: '1', defaultValue: 14 }],
    ROC: [{ key: 'period', label: 'ROC 기간', step: '1', defaultValue: 14 }],
    CCI: [{ key: 'period', label: 'CCI 기간', step: '1', defaultValue: 20 }],
    WILLIAMS_R: [{ key: 'period', label: '윌리엄스 %R 기간', step: '1', defaultValue: 14 }],
    PRICE_VS_SMA: [{ key: 'period', label: 'SMA 기간', step: '1', defaultValue: 20 }],
    PRICE_VS_EMA: [{ key: 'period', label: 'EMA 기간', step: '1', defaultValue: 20 }],
    PRICE_VS_VWAP: [{ key: 'period', label: 'VWAP 기준 기간', step: '1', defaultValue: 20 }],
    SMA_SLOPE: [
      { key: 'period', label: 'SMA 기간', step: '1', defaultValue: 20 },
      { key: 'lookback', label: '기울기 비교 봉 수', step: '1', defaultValue: 5 }
    ],
    OBV: [],
    OBV_SLOPE: [{ key: 'lookback', label: '기울기 비교 봉 수', step: '1', defaultValue: 5 }],
    VOLUME_RATIO: [{ key: 'period', label: '평균 거래량 기간', step: '1', defaultValue: 20 }],
    CMF: [{ key: 'period', label: 'CMF 기간', step: '1', defaultValue: 20 }],
    BOLLINGER_POS: [
      { key: 'period', label: '볼린저 기간', step: '1', defaultValue: 20 },
      { key: 'stddev', label: '표준편차', step: '0.1', defaultValue: 2 }
    ],
    ATR: [{ key: 'period', label: 'ATR 기간', step: '1', defaultValue: 14 }],
    ATR_PERCENT: [{ key: 'period', label: 'ATR 기간', step: '1', defaultValue: 14 }],
    PRICE_CHANGE: [{ key: 'bars', label: '비교 봉 수', step: '1', defaultValue: 5 }],
    VOLATILITY_20D: [{ key: 'period', label: '변동성 기간', step: '1', defaultValue: 20 }],
    CANDLE_BODY: []
  }

  let patterns = []
  let selectedPattern = null
  let workspace = null
  let selectedNode = { type: 'general' }
  let loading = true
  let saving = false
  let dirty = false
  let error = ''
  let notice = ''
  let showNewPattern = false
  let newPatternName = ''
  let validationIssues = []
  onMount(loadPatterns)

  function safeParse(value, fallback) {
    try {
      return value ? JSON.parse(value) : fallback
    } catch {
      return fallback
    }
  }

  function blankRule(template = {}) {
    const indicator = template.indicator ?? 'RSI'
    return {
      indicator,
      params: buildRuleParams(indicator, { ...(template.params ?? { period: 14 }) }),
      operator: template.operator ?? '>',
      value: template.value ?? 50,
      withinBars: 0,
      refSymbol: '',
      compareIndicator: '',
      compareParams: {},
      weight: 1,
      consecutiveBars: 0
    }
  }

  function blankGroup(label) {
    return {
      label: label ?? `매수 상황`,
      logic: 'AND',
      rules: [blankRule()]
    }
  }

  function blankExitGroup(label) {
    return {
      label: label ?? `매도 상황`,
      logic: 'AND',
      rules: [blankRule({ indicator: 'RSI', operator: '>=', value: 70, params: { period: 14 } })]
    }
  }

  function blankWeightTier() {
    return {
      label: `기본 매수 비중`,
      logic: 'AND',
      allocationPercent: 100,
      conditions: [blankRule()]
    }
  }

  function blankScalingRule() {
    return {
      direction: 'SCALE_IN',
      logic: 'AND',
      percent: 50,
      maxCount: 1,
      minProfitPercent: 0,
      conditions: [blankRule()]
    }
  }

  function normalizeRule(rule = {}) {
    const indicator = rule.indicator ?? rule.Indicator ?? 'RSI'
    const compareIndicator = rule.compareIndicator ?? rule.CompareIndicator ?? ''
    return {
      indicator,
      params: buildRuleParams(indicator, { ...(rule.params ?? rule.Params ?? {}) }),
      operator: rule.operator ?? rule.Operator ?? '>',
      value: Number(rule.value ?? rule.Value ?? 0),
      withinBars: Number(rule.withinBars ?? rule.WithinBars ?? 0),
      refSymbol: rule.refSymbol ?? rule.RefSymbol ?? '',
      compareIndicator,
      compareParams: compareIndicator ? buildRuleParams(compareIndicator, { ...(rule.compareParams ?? rule.CompareParams ?? {}) }) : sanitizeNumericMap(rule.compareParams ?? rule.CompareParams ?? {}),
      weight: Number(rule.weight ?? rule.Weight ?? 1),
      consecutiveBars: Number(rule.consecutiveBars ?? rule.ConsecutiveBars ?? 0)
    }
  }

  function normalizeGroup(group = {}) {
    return {
      label: group.label ?? group.Label ?? '매수 상황',
      logic: group.logic ?? group.Logic ?? 'AND',
      rules: (group.rules ?? group.Rules ?? []).map(normalizeRule)
    }
  }

  function normalizeWeightTier(tier = {}) {
    return {
      label: tier.label ?? tier.Label ?? '매수 비중',
      logic: tier.logic ?? tier.Logic ?? 'AND',
      allocationPercent: Number(tier.allocationPercent ?? tier.AllocationPercent ?? 100),
      conditions: (tier.conditions ?? tier.Conditions ?? []).map(normalizeRule)
    }
  }

  function normalizeScalingRule(rule = {}) {
    return {
      direction: rule.direction ?? rule.Direction ?? 'SCALE_IN',
      logic: rule.logic ?? rule.Logic ?? 'AND',
      percent: Number(rule.percent ?? rule.Percent ?? 50),
      maxCount: Number(rule.maxCount ?? rule.MaxCount ?? 1),
      minProfitPercent: Number(rule.minProfitPercent ?? rule.MinProfitPercent ?? 0),
      conditions: (rule.conditions ?? rule.Conditions ?? []).map(normalizeRule)
    }
  }

  function buildWorkspace(raw) {
    const entryGroups = safeParse(raw.entryGroupsJson, []).map(normalizeGroup)
    const flatRules = safeParse(raw.entryRulesJson, []).map(normalizeRule)
    const storedExitGroups = safeParse(raw.exitGroupsJson, []).map(normalizeGroup)
    const flatExitRules = safeParse(raw.exitRulesJson, []).map(normalizeRule)

    return {
      raw,
      name: raw.name ?? '',
      description: raw.description ?? '',
      isActive: raw.isActive ?? true,
      enableLiveTrading: raw.enableLiveTrading ?? false,
      requireBullRegime: !!raw.requireBullRegime,
      entryMode: raw.entryMode ?? 'CurrentClose',
      sizingMode: raw.sizingMode ?? 'FixedRisk',
      entryGroupsLogic: raw.entryGroupsLogic ?? raw.entryLogic ?? 'AND',
      exitGroupsLogic: raw.exitGroupsLogic ?? raw.exitRulesLogic ?? 'OR',
      atrStopMultiplier: Number(raw.atrStopMultiplier ?? 2),
      atrTargetMultiplier: Number(raw.atrTargetMultiplier ?? 3),
      maxHoldingBars: Number(raw.maxHoldingBars ?? 10),
      trailingAtr: Number(raw.trailingAtr ?? 0),
      partialProfitR: Number(raw.partialProfitR ?? 0),
      defaultAllocationPercent: Number(raw.defaultAllocationPercent ?? 100),
      useWeightTiers: !!raw.useWeightTiers,
      entryGroups: entryGroups.length > 0 ? entryGroups : (flatRules.length > 0 ? [{ label: '매수 상황 1', logic: raw.entryLogic ?? 'AND', rules: flatRules }] : [blankGroup('매수 상황 1')]),
      exitGroups: storedExitGroups.length > 0
        ? storedExitGroups
        : (flatExitRules.length > 0 ? [{ label: '매도 상황 1', logic: raw.exitRulesLogic ?? 'OR', rules: flatExitRules }] : []),
      weightTiers: safeParse(raw.weightTiersJson, []).map(normalizeWeightTier),
      scalingRules: safeParse(raw.scalingRulesJson, []).map(normalizeScalingRule),
      timeFilter: {
        allowedDaysOfWeek: safeParse(raw.timeFilterJson, {}).allowedDaysOfWeek ?? [],
        blockedMonths: safeParse(raw.timeFilterJson, {}).blockedMonths ?? []
      },
      circuitBreaker: {
        consecutiveLossLimit: Number(safeParse(raw.circuitBreakerJson, {}).consecutiveLossLimit ?? 0),
        cooldownBars: Number(safeParse(raw.circuitBreakerJson, {}).cooldownBars ?? 5),
        maxDrawdownPercent: Number(safeParse(raw.circuitBreakerJson, {}).maxDrawdownPercent ?? 0)
      },
      reentry: {
        cooldownBarsAfterLoss: Number(safeParse(raw.reentryJson, {}).cooldownBarsAfterLoss ?? 0),
        cooldownBarsAfterWin: Number(safeParse(raw.reentryJson, {}).cooldownBarsAfterWin ?? 0)
      },
      portfolioRules: {
        maxTotalPositions: Number(safeParse(raw.portfolioRulesJson, {}).maxTotalPositions ?? 0),
        maxSinglePositionPercent: Number(safeParse(raw.portfolioRulesJson, {}).maxSinglePositionPercent ?? 0),
        maxEntriesPerDay: Number(safeParse(raw.portfolioRulesJson, {}).maxEntriesPerDay ?? 0),
        maxCorrelation: Number(safeParse(raw.portfolioRulesJson, {}).maxCorrelation ?? 0)
      },
      dynamicExit: {
        stopType: safeParse(raw.dynamicExitJson, {}).stopType ?? 'ATR',
        stopParams: normalizeDynamicParams('stop', safeParse(raw.dynamicExitJson, {}).stopType ?? 'ATR', safeParse(raw.dynamicExitJson, {}).stopParams ?? {}),
        targetType: safeParse(raw.dynamicExitJson, {}).targetType ?? 'ATR',
        targetParams: normalizeDynamicParams('target', safeParse(raw.dynamicExitJson, {}).targetType ?? 'ATR', safeParse(raw.dynamicExitJson, {}).targetParams ?? {})
      }
    }
  }

  function touch() {
    workspace = { ...workspace }
    dirty = true
  }

  function toNumber(value, fallback = 0) {
    const parsed = Number(value)
    return Number.isFinite(parsed) ? parsed : fallback
  }

  function normalizeRuleParams(indicator, params = {}) {
    const normalized = { ...params }
    const upper = (indicator ?? '').toUpperCase()

    if (normalized.stdDev != null && normalized.stddev == null) {
      normalized.stddev = normalized.stdDev
    }
    delete normalized.stdDev

    if (['BREAKOUT_HIGH', 'BREAKOUT_LOW', 'DIST_FROM_HIGH', 'DIST_FROM_LOW'].includes(upper) && normalized.lookback != null && normalized.period == null) {
      normalized.period = normalized.lookback
    }
    delete normalized.lookback

    return normalized
  }

  function getIndicatorFieldConfigs(indicator) {
    return indicatorFieldConfigs[(indicator ?? '').toUpperCase()] ?? []
  }

  function buildRuleParams(indicator, params = {}, applyDefaults = true) {
    const normalized = normalizeRuleParams(indicator, sanitizeNumericMap(params))
    const configs = getIndicatorFieldConfigs(indicator)
    const next = { ...normalized }

    if (applyDefaults) {
      for (const field of configs) {
        if (next[field.key] == null) {
          next[field.key] = field.defaultValue
        }
      }
    }

    return next
  }

  function getExtraParamEntries(paramMap = {}, indicator) {
    const knownKeys = new Set(getIndicatorFieldConfigs(indicator).map((field) => field.key))
    return Object.entries(paramMap || {}).filter(([key]) => !knownKeys.has(key))
  }

  function sanitizeNumericMap(map = {}) {
    return Object.fromEntries(
      Object.entries(map)
        .map(([key, value]) => [key.trim(), toNumber(value, 0)])
        .filter(([key]) => key.length > 0)
    )
  }

  function getDynamicFieldConfigs(kind, type) {
    return dynamicExitFieldConfigs[kind]?.[type] ?? []
  }

  function normalizeDynamicParams(kind, type, params = {}) {
    const incoming = sanitizeNumericMap(params)
    const configs = getDynamicFieldConfigs(kind, type)
    const normalized = {}

    if (incoming.value != null && configs.length > 0 && configs.every((field) => incoming[field.key] == null)) {
      normalized[configs[0].key] = incoming.value
    }

    for (const field of configs) {
      normalized[field.key] = toNumber(incoming[field.key] ?? normalized[field.key] ?? field.defaultValue, field.defaultValue)
    }

    return normalized
  }

  function setDynamicExitType(kind, type) {
    if (!workspace) return

    if (kind === 'stop') {
      workspace.dynamicExit.stopType = type
      workspace.dynamicExit.stopParams = normalizeDynamicParams('stop', type, workspace.dynamicExit.stopParams)
    } else {
      workspace.dynamicExit.targetType = type
      workspace.dynamicExit.targetParams = normalizeDynamicParams('target', type, workspace.dynamicExit.targetParams)
    }

    touch()
  }

  function updateDynamicParam(kind, key, value) {
    if (!workspace) return

    if (kind === 'stop') {
      workspace.dynamicExit.stopParams = {
        ...workspace.dynamicExit.stopParams,
        [key]: toNumber(value, workspace.dynamicExit.stopParams[key] ?? 0)
      }
    } else {
      workspace.dynamicExit.targetParams = {
        ...workspace.dynamicExit.targetParams,
        [key]: toNumber(value, workspace.dynamicExit.targetParams[key] ?? 0)
      }
    }

    touch()
  }

  function sanitizeRule(rule) {
    return {
      ...rule,
      indicator: rule.indicator,
      params: buildRuleParams(rule.indicator, rule.params, false),
      value: toNumber(rule.value, 0),
      withinBars: Math.max(0, toNumber(rule.withinBars, 0)),
      refSymbol: (rule.refSymbol ?? '').trim(),
      compareIndicator: (rule.compareIndicator ?? '').trim(),
      compareParams: rule.compareIndicator ? buildRuleParams(rule.compareIndicator, rule.compareParams, false) : sanitizeNumericMap(rule.compareParams),
      weight: toNumber(rule.weight, 1),
      consecutiveBars: Math.max(0, toNumber(rule.consecutiveBars, 0))
    }
  }

  function buildPatternPayload(currentWorkspace) {
    if (!currentWorkspace) return null
    const entryGroups = currentWorkspace.entryGroups.map((group) => ({
      ...group,
      rules: group.rules.map(sanitizeRule)
    }))
    const exitGroups = currentWorkspace.exitGroups.map((group) => ({
      ...group,
      rules: group.rules.map(sanitizeRule)
    }))
    const weightTiers = currentWorkspace.weightTiers.map((tier) => ({
      ...tier,
      allocationPercent: toNumber(tier.allocationPercent, 100),
      conditions: tier.conditions.map(sanitizeRule)
    }))
    const scalingRules = currentWorkspace.scalingRules.map((rule) => ({
      ...rule,
      percent: toNumber(rule.percent, 50),
      maxCount: toNumber(rule.maxCount, 1),
      minProfitPercent: toNumber(rule.minProfitPercent, 0),
      conditions: rule.conditions.map(sanitizeRule)
    }))
    const dynamicExit = {
      stopType: currentWorkspace.dynamicExit.stopType,
      stopParams: normalizeDynamicParams('stop', currentWorkspace.dynamicExit.stopType, currentWorkspace.dynamicExit.stopParams),
      targetType: currentWorkspace.dynamicExit.targetType,
      targetParams: normalizeDynamicParams('target', currentWorkspace.dynamicExit.targetType, currentWorkspace.dynamicExit.targetParams)
    }

    return {
      ...currentWorkspace.raw,
      name: currentWorkspace.name,
      description: currentWorkspace.description,
      isActive: currentWorkspace.isActive,
      enableLiveTrading: currentWorkspace.enableLiveTrading,
      requireBullRegime: currentWorkspace.requireBullRegime,
      entryMode: currentWorkspace.entryMode,
      sizingMode: currentWorkspace.sizingMode,
      entryLogic: currentWorkspace.entryGroupsLogic,
      entryGroupsLogic: currentWorkspace.entryGroupsLogic,
      exitRulesLogic: currentWorkspace.exitGroupsLogic,
      exitGroupsLogic: currentWorkspace.exitGroupsLogic,
      atrStopMultiplier: toNumber(currentWorkspace.atrStopMultiplier, 2),
      atrTargetMultiplier: toNumber(currentWorkspace.atrTargetMultiplier, 3),
      maxHoldingBars: toNumber(currentWorkspace.maxHoldingBars, 10),
      trailingAtr: toNumber(currentWorkspace.trailingAtr, 0),
      partialProfitR: toNumber(currentWorkspace.partialProfitR, 0),
      defaultAllocationPercent: toNumber(currentWorkspace.defaultAllocationPercent, 100),
      useWeightTiers: currentWorkspace.useWeightTiers,
      entryRulesJson: JSON.stringify([]),
      entryGroupsJson: JSON.stringify(entryGroups),
      exitRulesJson: JSON.stringify([]),
      exitGroupsJson: JSON.stringify(exitGroups),
      weightTiersJson: JSON.stringify(weightTiers),
      scalingRulesJson: JSON.stringify(scalingRules),
      timeFilterJson: JSON.stringify(currentWorkspace.timeFilter),
      circuitBreakerJson: JSON.stringify(currentWorkspace.circuitBreaker),
      reentryJson: JSON.stringify(currentWorkspace.reentry),
      portfolioRulesJson: JSON.stringify(currentWorkspace.portfolioRules),
      dynamicExitJson: JSON.stringify(dynamicExit)
    }
  }

  function collectValidationIssues(currentWorkspace) {
    if (!currentWorkspace) return []

    const issues = []
    const checkRule = (rule, scope) => {
      const indicator = (rule.indicator ?? '').trim().toUpperCase()
      if (!indicatorSet.has(indicator)) {
        issues.push(`${scope}: 지원하지 않는 지표 ${rule.indicator || '(비어 있음)'}`)
      }
      if (toNumber(rule.withinBars, 0) > 0 && toNumber(rule.consecutiveBars, 0) > 0) {
        issues.push(`${scope}: 최근 N봉 내와 연속 봉 수는 동시에 사용할 수 없습니다.`)
      }
      if (toNumber(rule.withinBars, 0) < 0 || toNumber(rule.consecutiveBars, 0) < 0) issues.push(`${scope}: 봉 수는 0 이상이어야 합니다.`)
      if (toNumber(rule.weight, 0) <= 0) issues.push(`${scope}: 가중치는 0보다 커야 합니다.`)
      if (Object.values(rule.params ?? {}).some((value) => toNumber(value, -1) < 0)) issues.push(`${scope}: 지표 계산값은 음수일 수 없습니다.`)
    }

    if (!currentWorkspace.name.trim()) issues.push('전략 이름을 입력하세요.')
    if (toNumber(currentWorkspace.atrStopMultiplier, 0) <= 0) issues.push('ATR 손절 배수는 0보다 커야 합니다.')
    if (toNumber(currentWorkspace.atrTargetMultiplier, 0) <= 0) issues.push('ATR 목표 배수는 0보다 커야 합니다.')
    if (toNumber(currentWorkspace.maxHoldingBars, -1) < 0) issues.push('최대 보유 봉 수는 0 이상이어야 합니다.')
    if (toNumber(currentWorkspace.trailingAtr, -1) < 0 || toNumber(currentWorkspace.partialProfitR, -1) < 0) issues.push('트레일링 ATR과 부분 익절 R은 0 이상이어야 합니다.')
    if (toNumber(currentWorkspace.defaultAllocationPercent, -1) < 0 || toNumber(currentWorkspace.defaultAllocationPercent, 101) > 100) issues.push('기본 매수 비중은 0~100%여야 합니다.')

    if (!currentWorkspace.entryGroups.length) {
      issues.push('매수 조건 묶음이 최소 1개는 필요합니다.')
    }

    currentWorkspace.entryGroups.forEach((group, groupIndex) => {
      if (!group.rules.length) {
        issues.push(`매수 상황 ${groupIndex + 1}: 조건이 비어 있습니다.`)
      }
      group.rules.forEach((rule, ruleIndex) => checkRule(rule, `매수 상황 ${groupIndex + 1} / 조건 ${ruleIndex + 1}`))
    })

    currentWorkspace.exitGroups.forEach((group, groupIndex) => {
      if (!group.rules.length) {
        issues.push(`매도 상황 ${groupIndex + 1}: 조건이 비어 있습니다.`)
      }
      group.rules.forEach((rule, ruleIndex) => checkRule(rule, `매도 상황 ${groupIndex + 1} / 조건 ${ruleIndex + 1}`))
    })

    currentWorkspace.weightTiers.forEach((tier, tierIndex) => {
      if (toNumber(tier.allocationPercent, -1) < 0 || toNumber(tier.allocationPercent, 101) > 100) issues.push(`매수 비중 ${tierIndex + 1}: 비중은 0~100%여야 합니다.`)
      if (!tier.conditions.length) {
        issues.push(`매수 비중 ${tierIndex + 1}: 조건이 비어 있습니다.`)
      }
      tier.conditions.forEach((rule, ruleIndex) => checkRule(rule, `매수 비중 ${tierIndex + 1} / 조건 ${ruleIndex + 1}`))
    })

    currentWorkspace.scalingRules.forEach((scalingRule, scalingIndex) => {
      if (toNumber(scalingRule.percent, 0) <= 0 || toNumber(scalingRule.percent, 101) > 100) issues.push(`추가 매수·분할 매도 ${scalingIndex + 1}: 비율은 0 초과 100% 이하여야 합니다.`)
      if (toNumber(scalingRule.maxCount, 0) < 1) issues.push(`추가 매수·분할 매도 ${scalingIndex + 1}: 최대 횟수는 1 이상이어야 합니다.`)
      if (!scalingRule.conditions.length) {
        issues.push(`추가 매수·분할 매도 ${scalingIndex + 1}: 조건이 비어 있습니다.`)
      }
      scalingRule.conditions.forEach((rule, ruleIndex) => checkRule(rule, `추가 매수·분할 매도 ${scalingIndex + 1} / 조건 ${ruleIndex + 1}`))
    })

    if (currentWorkspace.circuitBreaker.consecutiveLossLimit < 0 || currentWorkspace.circuitBreaker.cooldownBars < 0) issues.push('손실 횟수와 거래 중단 봉 수는 0 이상이어야 합니다.')
    if (currentWorkspace.circuitBreaker.maxDrawdownPercent < 0 || currentWorkspace.circuitBreaker.maxDrawdownPercent > 100) issues.push('최대 낙폭은 0~100%여야 합니다.')
    if (currentWorkspace.reentry.cooldownBarsAfterLoss < 0 || currentWorkspace.reentry.cooldownBarsAfterWin < 0) issues.push('재매수 대기 봉 수는 0 이상이어야 합니다.')
    if (currentWorkspace.portfolioRules.maxTotalPositions < 0 || currentWorkspace.portfolioRules.maxEntriesPerDay < 0) issues.push('보유 종목 수와 하루 매수 횟수는 0 이상이어야 합니다.')
    if (currentWorkspace.portfolioRules.maxSinglePositionPercent < 0 || currentWorkspace.portfolioRules.maxSinglePositionPercent > 100) issues.push('한 종목 최대 비중은 0~100%여야 합니다.')
    if (currentWorkspace.portfolioRules.maxCorrelation < 0 || currentWorkspace.portfolioRules.maxCorrelation > 1) issues.push('최대 상관계수는 0~1 사이여야 합니다.')

    return issues
  }

  $: validationIssues = workspace ? collectValidationIssues(workspace) : []
  $: previewPattern = workspace ? buildPatternPayload(workspace) : null
  $: previewSelectedSummary = getSelectedPreviewSummary()

  async function loadPatterns() {
    loading = true
    try {
      const res = await patternApi.list()
      patterns = res.data || []
      error = ''
    } catch (e) {
      error = e?.message || '전략 목록을 불러오지 못했습니다.'
    } finally {
      loading = false
    }
  }

  async function createPattern() {
    if (!newPatternName.trim()) return
    if (dirty && !confirm('저장하지 않은 변경이 있습니다. 새 전략을 만들까요?')) return
    try {
      const res = await patternApi.create({ name: newPatternName, description: '' })
      newPatternName = ''
      showNewPattern = false
      await loadPatterns()
      selectedPattern = res.data
      workspace = buildWorkspace(res.data.raw)
      selectedNode = { type: 'general' }
      dirty = false
      notice = '새 매매 전략을 만들었습니다.'
      error = ''
    } catch (e) {
      error = e?.message || '전략 생성에 실패했습니다.'
    }
  }

  async function selectPattern(pat) {
    if (selectedPattern?.id !== pat.id && dirty && !confirm('저장하지 않은 변경이 있습니다. 다른 전략을 불러올까요?')) return
    try {
      const res = await patternApi.get(pat.id)
      selectedPattern = res.data
      workspace = buildWorkspace(res.data.raw)
      selectedNode = { type: 'general' }
      dirty = false
      notice = ''
      error = ''
    } catch (e) {
      error = e?.message || '전략을 불러오지 못했습니다.'
    }
  }

  async function savePattern() {
    if (!workspace?.name?.trim()) {
      error = '전략 이름을 입력하세요.'
      return
    }
    if (validationIssues.length > 0) {
      error = validationIssues.join('\n')
      return
    }

    saving = true
    try {
      const payload = buildPatternPayload(workspace)

      const res = await patternApi.update(selectedPattern.id, payload)
      selectedPattern = res.data
      workspace = buildWorkspace(res.data.raw)
      await loadPatterns()
      dirty = false
      notice = '매매 전략을 저장했습니다.'
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '전략 저장에 실패했습니다.'
    } finally {
      saving = false
    }
  }

  async function deletePattern(pat) {
    if (selectedPattern?.id === pat.id && dirty && !confirm('저장하지 않은 변경도 함께 사라집니다. 계속할까요?')) return
    if (!confirm(`"${pat.name}" 전략을 삭제할까요?`)) return
    try {
      await patternApi.delete(pat.id)
      if (selectedPattern?.id === pat.id) {
        selectedPattern = null
        workspace = null
        selectedNode = { type: 'general' }
      }
      await loadPatterns()
    } catch (e) {
      error = e?.message || '전략 삭제에 실패했습니다.'
    }
  }

  function selectedGroupIndex() {
    if (selectedNode.type === 'group' || selectedNode.type === 'entryRule') return selectedNode.groupIndex
    return workspace?.entryGroups?.length ? 0 : -1
  }

  function selectedExitGroupIndex() {
    if (selectedNode.type === 'exitGroup' || selectedNode.type === 'exitRule') return selectedNode.groupIndex
    return workspace?.exitGroups?.length ? 0 : -1
  }

  function addRuleToGroup(template = {}) {
    let index = selectedGroupIndex()
    if (index < 0) {
      workspace.entryGroups.push(blankGroup(`매수 상황 1`))
      index = 0
    }
    workspace.entryGroups[index].rules.push(blankRule(template))
    selectedNode = { type: 'entryRule', groupIndex: index, ruleIndex: workspace.entryGroups[index].rules.length - 1 }
    touch()
  }

  function addRuleToExitGroup(template = {}) {
    let index = selectedExitGroupIndex()
    if (index < 0) {
      workspace.exitGroups.push(blankExitGroup('매도 상황 1'))
      index = 0
    } else {
      workspace.exitGroups[index].rules.push(blankRule(template))
    }
    selectedNode = { type: 'exitRule', groupIndex: index, ruleIndex: workspace.exitGroups[index].rules.length - 1 }
    touch()
  }

  function addNode(kind) {
    if (!workspace) return

    if (kind === 'group') {
      workspace.entryGroups.push(blankGroup(`매수 상황 ${workspace.entryGroups.length + 1}`))
      selectedNode = { type: 'group', groupIndex: workspace.entryGroups.length - 1 }
    } else if (kind === 'exitGroup') {
      workspace.exitGroups.push(blankExitGroup(`매도 상황 ${workspace.exitGroups.length + 1}`))
      selectedNode = { type: 'exitGroup', groupIndex: workspace.exitGroups.length - 1 }
    } else if (kind === 'weightTier') {
      workspace.weightTiers.push(blankWeightTier())
      workspace.useWeightTiers = true
      selectedNode = { type: 'weightTier', tierIndex: workspace.weightTiers.length - 1 }
    } else if (kind === 'scalingRule') {
      workspace.scalingRules.push(blankScalingRule())
      selectedNode = { type: 'scalingRule', scalingIndex: workspace.scalingRules.length - 1 }
    }

    touch()
  }

  function addTierCondition(tierIndex) {
    workspace.weightTiers[tierIndex].conditions.push(blankRule())
    selectedNode = { type: 'tierRule', tierIndex, ruleIndex: workspace.weightTiers[tierIndex].conditions.length - 1 }
    touch()
  }

  function addScalingCondition(scalingIndex) {
    workspace.scalingRules[scalingIndex].conditions.push(blankRule())
    selectedNode = { type: 'scalingRuleCondition', scalingIndex, ruleIndex: workspace.scalingRules[scalingIndex].conditions.length - 1 }
    touch()
  }

  function removeNode(node) {
    if (!workspace) return

    if (node.type === 'group') {
      workspace.entryGroups.splice(node.groupIndex, 1)
      selectedNode = { type: 'general' }
    } else if (node.type === 'entryRule') {
      workspace.entryGroups[node.groupIndex].rules.splice(node.ruleIndex, 1)
      selectedNode = { type: 'group', groupIndex: node.groupIndex }
    } else if (node.type === 'exitGroup') {
      workspace.exitGroups.splice(node.groupIndex, 1)
      selectedNode = { type: 'general' }
    } else if (node.type === 'exitRule') {
      workspace.exitGroups[node.groupIndex].rules.splice(node.ruleIndex, 1)
      selectedNode = { type: 'exitGroup', groupIndex: node.groupIndex }
    } else if (node.type === 'weightTier') {
      workspace.weightTiers.splice(node.tierIndex, 1)
      selectedNode = { type: 'general' }
    } else if (node.type === 'tierRule') {
      workspace.weightTiers[node.tierIndex].conditions.splice(node.ruleIndex, 1)
      selectedNode = { type: 'weightTier', tierIndex: node.tierIndex }
    } else if (node.type === 'scalingRule') {
      workspace.scalingRules.splice(node.scalingIndex, 1)
      selectedNode = { type: 'general' }
    } else if (node.type === 'scalingRuleCondition') {
      workspace.scalingRules[node.scalingIndex].conditions.splice(node.ruleIndex, 1)
      selectedNode = { type: 'scalingRule', scalingIndex: node.scalingIndex }
    }

    touch()
  }

  function cloneValue(value) {
    return JSON.parse(JSON.stringify(value))
  }

  function moveItem(list, index, offset) {
    const next = index + offset
    if (next < 0 || next >= list.length) return index
    const [item] = list.splice(index, 1)
    list.splice(next, 0, item)
    touch()
    return next
  }

  function moveNode(node, offset) {
    if (node.type === 'group') selectedNode = { ...node, groupIndex: moveItem(workspace.entryGroups, node.groupIndex, offset) }
    else if (node.type === 'entryRule') selectedNode = { ...node, ruleIndex: moveItem(workspace.entryGroups[node.groupIndex].rules, node.ruleIndex, offset) }
    else if (node.type === 'exitGroup') selectedNode = { ...node, groupIndex: moveItem(workspace.exitGroups, node.groupIndex, offset) }
    else if (node.type === 'exitRule') selectedNode = { ...node, ruleIndex: moveItem(workspace.exitGroups[node.groupIndex].rules, node.ruleIndex, offset) }
    else if (node.type === 'weightTier') selectedNode = { ...node, tierIndex: moveItem(workspace.weightTiers, node.tierIndex, offset) }
    else if (node.type === 'tierRule') selectedNode = { ...node, ruleIndex: moveItem(workspace.weightTiers[node.tierIndex].conditions, node.ruleIndex, offset) }
    else if (node.type === 'scalingRule') selectedNode = { ...node, scalingIndex: moveItem(workspace.scalingRules, node.scalingIndex, offset) }
    else if (node.type === 'scalingRuleCondition') selectedNode = { ...node, ruleIndex: moveItem(workspace.scalingRules[node.scalingIndex].conditions, node.ruleIndex, offset) }
  }

  function duplicateNode(node) {
    if (node.type === 'entryRule') workspace.entryGroups[node.groupIndex].rules.splice(node.ruleIndex + 1, 0, cloneValue(workspace.entryGroups[node.groupIndex].rules[node.ruleIndex]))
    else if (node.type === 'exitRule') workspace.exitGroups[node.groupIndex].rules.splice(node.ruleIndex + 1, 0, cloneValue(workspace.exitGroups[node.groupIndex].rules[node.ruleIndex]))
    else if (node.type === 'tierRule') workspace.weightTiers[node.tierIndex].conditions.splice(node.ruleIndex + 1, 0, cloneValue(workspace.weightTiers[node.tierIndex].conditions[node.ruleIndex]))
    else if (node.type === 'scalingRuleCondition') workspace.scalingRules[node.scalingIndex].conditions.splice(node.ruleIndex + 1, 0, cloneValue(workspace.scalingRules[node.scalingIndex].conditions[node.ruleIndex]))
    else if (node.type === 'group') workspace.entryGroups.splice(node.groupIndex + 1, 0, cloneValue(workspace.entryGroups[node.groupIndex]))
    else if (node.type === 'exitGroup') workspace.exitGroups.splice(node.groupIndex + 1, 0, cloneValue(workspace.exitGroups[node.groupIndex]))
    else if (node.type === 'weightTier') workspace.weightTiers.splice(node.tierIndex + 1, 0, cloneValue(workspace.weightTiers[node.tierIndex]))
    else if (node.type === 'scalingRule') workspace.scalingRules.splice(node.scalingIndex + 1, 0, cloneValue(workspace.scalingRules[node.scalingIndex]))
    touch()
  }

  function ruleSummary(rule) {
    const indicatorLabel = indicatorLabels[rule.indicator] ?? rule.indicator
    const params = Object.entries(rule.params || {}).map(([key, value]) => `${paramKeyLabels[key] ?? key}:${value}`).join(', ')
    const compare = rule.compareIndicator
      ? ` 대비 ${indicatorLabels[rule.compareIndicator] ?? rule.compareIndicator}`
      : ` ${operatorLabels[rule.operator] ?? rule.operator} ${rule.value}`
    const meta = [rule.withinBars ? `최근 ${rule.withinBars}봉 내` : '', rule.consecutiveBars ? `${rule.consecutiveBars}봉 연속` : ''].filter(Boolean).join(' · ')
    return `${indicatorLabel}${params ? `(${params})` : ''}${compare}${meta ? ` · ${meta}` : ''}`
  }

  function displayEntryMode(value) {
    return entryModeLabels[value] ?? value
  }

  function displaySizingMode(value) {
    return sizingModeLabels[value] ?? value
  }

  function displayLogic(value) {
    return logicLabels[value] ?? value
  }

  function displayScalingDirection(value) {
    return scalingDirectionLabels[value] ?? value
  }

  function displayStopType(value) {
    return stopTypeLabels[value] ?? value
  }

  function displayTargetType(value) {
    return targetTypeLabels[value] ?? value
  }

  function tooltipFor(key) {
    return glossaryTooltips[key] ?? ''
  }

  function selectNode(node) {
    selectedNode = node
  }

  function getCurrentRule() {
    if (!workspace) return null
    if (selectedNode.type === 'entryRule') return workspace.entryGroups[selectedNode.groupIndex]?.rules[selectedNode.ruleIndex] ?? null
    if (selectedNode.type === 'exitRule') return workspace.exitGroups[selectedNode.groupIndex]?.rules[selectedNode.ruleIndex] ?? null
    if (selectedNode.type === 'tierRule') return workspace.weightTiers[selectedNode.tierIndex]?.conditions[selectedNode.ruleIndex] ?? null
    if (selectedNode.type === 'scalingRuleCondition') return workspace.scalingRules[selectedNode.scalingIndex]?.conditions[selectedNode.ruleIndex] ?? null
    return null
  }

  function getSelectedPreviewSummary() {
    if (!workspace) return ''
    const rule = getCurrentRule()
    if (rule) return ruleSummary(rule)
    if (selectedNode.type === 'dynamicExit') {
      return `손절 ${displayStopType(workspace.dynamicExit.stopType)} · 목표 ${displayTargetType(workspace.dynamicExit.targetType)}`
    }
    if (selectedNode.type === 'general') {
      return `${displayEntryMode(workspace.entryMode)} · 손절 ${workspace.atrStopMultiplier} ATR · 목표 ${workspace.atrTargetMultiplier} ATR`
    }
    if (selectedNode.type === 'group') {
      const group = workspace.entryGroups[selectedNode.groupIndex]
      return `${group?.label ?? '매수 상황'} · ${displayLogic(group?.logic)}`
    }
    if (selectedNode.type === 'exitGroup') {
      const group = workspace.exitGroups[selectedNode.groupIndex]
      return `${group?.label ?? '매도 상황'} · ${displayLogic(group?.logic)}`
    }
    return ''
  }

  function updateRuleField(field, value) {
    const rule = getCurrentRule()
    if (!rule) return
    rule[field] = field === 'value' || field === 'withinBars' || field === 'weight' || field === 'consecutiveBars'
      ? toNumber(value, 0)
      : value

    if (field === 'indicator') {
      rule.params = buildRuleParams(rule.indicator, rule.params)
    }
    if (field === 'compareIndicator') {
      rule.compareParams = rule.compareIndicator ? buildRuleParams(rule.compareIndicator, rule.compareParams) : {}
    }

    touch()
  }

  function addRuleMapEntry(field) {
    const rule = getCurrentRule()
    if (!rule) return
    rule[field] = { ...(rule[field] || {}), newKey: 0 }
    touch()
  }

  function updateRuleMapEntry(field, oldKey, nextKey, nextValue) {
    const rule = getCurrentRule()
    if (!rule) return
    const next = { ...(rule[field] || {}) }
    const value = nextValue ?? next[oldKey] ?? 0
    delete next[oldKey]
    next[nextKey || oldKey] = toNumber(value, 0)
    rule[field] = next
    touch()
  }

  function removeRuleMapEntry(field, key) {
    const rule = getCurrentRule()
    if (!rule) return
    const next = { ...(rule[field] || {}) }
    delete next[key]
    rule[field] = next
    touch()
  }

  function listToText(list) {
    return (list || []).join(', ')
  }

  function textToIntList(value) {
    return value.split(',').map((item) => item.trim()).filter(Boolean).map((item) => Number(item)).filter((item) => Number.isFinite(item))
  }
</script>

<div class="flex h-full overflow-hidden">
  <aside class="flex w-80 shrink-0 flex-col border-r border-gray-800 bg-gray-950">
    <div class="border-b border-gray-800 p-6">
      <div class="mb-2 flex items-center gap-3">
        <FolderTree size={20} class="text-blue-400" />
        <h2 class="text-2xl font-bold">내 매매 전략</h2>
        <span title={tooltipFor('workspace')} class="cursor-help text-gray-500 transition hover:text-blue-300">
          <CircleHelp size={16} />
        </span>
      </div>
      <p class="text-sm text-gray-400">언제 사고, 얼마나 사고, 언제 팔지 순서대로 정합니다.</p>
    </div>

    <div class="border-b border-gray-800 p-4">
      {#if !showNewPattern}
        <button on:click={() => (showNewPattern = true)} class="flex w-full items-center justify-center gap-2 rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">
          <Plus size={16} />
          새 전략
        </button>
      {:else}
        <div class="space-y-2">
          <input bind:value={newPatternName} placeholder="전략 이름" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-sm text-white" />
          <div class="flex gap-2">
            <button on:click={createPattern} class="flex-1 rounded bg-green-600 px-3 py-2 text-sm text-white transition hover:bg-green-700">생성</button>
            <button on:click={() => (showNewPattern = false)} class="flex-1 rounded bg-gray-700 px-3 py-2 text-sm text-white transition hover:bg-gray-600">취소</button>
          </div>
        </div>
      {/if}
    </div>

    <div class="flex-1 overflow-y-auto p-4">
      <div class="mb-3 text-xs uppercase tracking-wider text-gray-500">저장한 전략</div>
      {#if loading}
        <div class="text-sm text-gray-400">불러오는 중...</div>
      {:else}
        <div class="space-y-2">
          {#each patterns as pat}
            <div class={`rounded-lg border p-3 ${selectedPattern?.id === pat.id ? 'border-blue-600 bg-blue-950/30' : 'border-gray-800 bg-gray-900'}`}>
              <button on:click={() => selectPattern(pat)} class="w-full text-left">
                <div class="font-medium text-white">{pat.name}</div>
                <div class="mt-1 text-xs text-gray-500">{pat.raw?.updatedAt ?? pat.updatedAt}</div>
              </button>
              <div class="mt-2 flex justify-end">
                <button on:click={() => deletePattern(pat)} class="rounded p-1 text-red-400 transition hover:bg-red-950/30">
                  <Trash2 size={14} />
                </button>
              </div>
            </div>
          {/each}
        </div>
      {/if}
    </div>
    <div class="border-t border-gray-800 p-4 text-xs text-gray-500">
      가운데 매매 규칙에서 조건을 선택하면 오른쪽에서 수치를 바꿀 수 있습니다.
    </div>
  </aside>

  <section class="flex min-w-0 flex-1 flex-col border-r border-gray-800 bg-gray-900">
    {#if !workspace}
      <div class="flex h-full items-center justify-center text-gray-400">
        <div class="text-center">
          <ChevronRight size={48} class="mx-auto mb-4 opacity-50" />
          <p>왼쪽에서 전략을 선택하면 매매 규칙이 열립니다.</p>
        </div>
      </div>
    {:else}
      <div class="flex items-center justify-between border-b border-gray-800 px-6 py-4">
        <div>
          <div class="flex items-center gap-2 text-sm uppercase tracking-wider text-gray-500">
            <span title={tooltipFor('strategy')} class="cursor-help">매매 규칙</span>
            <span title={tooltipFor('strategy')} class="cursor-help text-gray-600 transition hover:text-blue-300">
              <CircleHelp size={14} />
            </span>
          </div>
          <h3 class="mt-1 text-2xl font-bold">{workspace.name || '이름 없는 전략'}</h3>
        </div>
        <div class="flex items-center gap-3">
          {#if dirty}
            <span class="rounded bg-amber-950/60 px-3 py-1 text-xs text-amber-300">미저장 변경</span>
          {/if}
          <button on:click={savePattern} disabled={saving || validationIssues.length > 0} class="flex items-center gap-2 rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700 disabled:opacity-50">
            <Save size={16} />
            {saving ? '저장 중...' : '저장'}
          </button>
        </div>
      </div>

      <div class="flex-1 overflow-auto p-6">
        {#if error}
          <div class="mb-4 rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
        {/if}
        {#if notice}
          <div class="mb-4 rounded-lg border border-green-700 bg-green-900/20 p-4 text-green-300">{notice}</div>
        {/if}
        {#if validationIssues.length > 0}
          <div class="mb-4 rounded-lg border border-amber-700 bg-amber-900/20 p-4 text-amber-200">
            <div class="mb-2 text-sm font-semibold">저장 전 수정할 항목</div>
            <div class="space-y-1 text-sm">
              {#each validationIssues as issue}
                <div>{issue}</div>
              {/each}
            </div>
          </div>
        {/if}

        <PatternPreview pattern={previewPattern} selectedRuleSummary={previewSelectedSummary} />

        <div class="mb-6 rounded-xl border border-gray-800 bg-gray-950 p-5">
          <button on:click={() => selectNode({ type: 'general' })} class={`w-full text-left ${selectedNode.type === 'general' ? 'text-blue-300' : 'text-white'}`}>
            <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
              <span title={tooltipFor('pattern')} class="cursor-help">전략 기본 설정</span>
              <span title={tooltipFor('pattern')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                <CircleHelp size={12} />
              </span>
            </div>
            <div class="mt-1 text-xl font-semibold">{workspace.name}</div>
            <div class="mt-2 flex flex-wrap gap-2 text-xs">
              <span class="rounded bg-gray-800 px-2 py-1">매수 시점: {displayEntryMode(workspace.entryMode)}</span>
              <span class="rounded bg-gray-800 px-2 py-1">주문 금액: {displaySizingMode(workspace.sizingMode)}</span>
              <span class="rounded bg-gray-800 px-2 py-1">{workspace.isActive ? '연구 사용 중' : '연구 제외'}</span>
              <span class={`rounded px-2 py-1 ${workspace.enableLiveTrading ? 'bg-amber-900/50 text-amber-200' : 'bg-gray-800'}`}>{workspace.enableLiveTrading ? '실시간 주문 연결' : '실시간 주문 꺼짐'}</span>
              <span class="rounded bg-gray-800 px-2 py-1">{workspace.requireBullRegime ? '강세장만 허용' : '장세 무관'}</span>
            </div>
          </button>
        </div>

        <div class="space-y-5">
          <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
            <div class="mb-4 flex items-center justify-between">
              <button on:click={() => selectNode({ type: 'entryRoot' })} class="text-left">
                <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                  <span title={tooltipFor('entryGroup')} class="cursor-help">언제 살까?</span>
                  <span title={tooltipFor('entryGroup')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                    <CircleHelp size={12} />
                  </span>
                </div>
                <div class="text-lg font-semibold">매수 상황 중 {displayLogic(workspace.entryGroupsLogic)}</div>
              </button>
              <button on:click={() => addNode('group')} class="rounded bg-gray-800 px-3 py-1 text-xs text-white transition hover:bg-gray-700">+ 매수 상황</button>
            </div>

            <div class="space-y-3">
              {#each workspace.entryGroups as group, groupIndex}
                <div class="rounded-lg border border-gray-800 bg-gray-900 p-4">
                  <div class="mb-3 flex items-center justify-between">
                    <button on:click={() => selectNode({ type: 'group', groupIndex })} class={`text-left ${selectedNode.type === 'group' && selectedNode.groupIndex === groupIndex ? 'text-blue-300' : 'text-white'}`}>
                      <div class="font-semibold">{group.label || `매수 상황 ${groupIndex + 1}`}</div>
                      <div class="mt-1 text-xs text-gray-500">조건을 {displayLogic(group.logic)} • {group.rules.length}개</div>
                    </button>
                    <div class="flex items-center gap-2">
                      <button on:click={() => addRuleToGroup({})} class="rounded bg-gray-800 px-2 py-1 text-xs text-white transition hover:bg-gray-700">+ 매수 조건</button>
                      <button title="위로" on:click={() => moveNode({ type: 'group', groupIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                      <button title="아래로" on:click={() => moveNode({ type: 'group', groupIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                      <button title="복제" on:click={() => duplicateNode({ type: 'group', groupIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                      <button on:click={() => removeNode({ type: 'group', groupIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                    </div>
                  </div>
                  <div class="space-y-2 border-l border-gray-800 pl-4">
                    {#each group.rules as rule, ruleIndex}
                      <div class="flex items-center gap-1">
                        <button on:click={() => selectNode({ type: 'entryRule', groupIndex, ruleIndex })} class={`min-w-0 flex-1 rounded border px-3 py-3 text-left text-sm transition ${selectedNode.type === 'entryRule' && selectedNode.groupIndex === groupIndex && selectedNode.ruleIndex === ruleIndex ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-950 text-gray-200 hover:border-gray-700'}`}>
                          <div title={tooltipFor('rule')} class="text-xs text-gray-400 cursor-help">매수 조건 {ruleIndex + 1}</div>
                          <div class="mt-1">{ruleSummary(rule)}</div>
                        </button>
                        <button title="위로" on:click={() => moveNode({ type: 'entryRule', groupIndex, ruleIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                        <button title="아래로" on:click={() => moveNode({ type: 'entryRule', groupIndex, ruleIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                        <button title="복제" on:click={() => duplicateNode({ type: 'entryRule', groupIndex, ruleIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                        <button title="삭제" on:click={() => removeNode({ type: 'entryRule', groupIndex, ruleIndex })} class="rounded p-1 text-red-400 hover:bg-red-950/30"><Trash2 size={13} /></button>
                      </div>
                    {/each}
                  </div>
                </div>
              {/each}
            </div>
          </div>

          <div class="grid grid-cols-2 gap-5">
            <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
              <div class="mb-4 flex items-center justify-between">
                <button on:click={() => selectNode({ type: 'exitRoot' })} class="text-left">
                  <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                    <span title={tooltipFor('exitRule')} class="cursor-help">언제 팔까?</span>
                    <span title={tooltipFor('exitRule')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                      <CircleHelp size={12} />
                    </span>
                  </div>
                  <div class="text-lg font-semibold">매도 상황 중 {displayLogic(workspace.exitGroupsLogic)}</div>
                </button>
                <button on:click={() => addNode('exitGroup')} class="rounded bg-gray-800 px-3 py-1 text-xs text-white transition hover:bg-gray-700">+ 매도 상황</button>
              </div>
              <div class="space-y-3">
                {#each workspace.exitGroups as group, groupIndex}
                  <div class="rounded-lg border border-gray-800 bg-gray-900 p-3">
                    <div class="mb-2 flex items-center justify-between gap-2">
                      <button on:click={() => selectNode({ type: 'exitGroup', groupIndex })} class={`text-left ${selectedNode.type === 'exitGroup' && selectedNode.groupIndex === groupIndex ? 'text-blue-300' : 'text-white'}`}>
                        <div class="font-semibold">{group.label || `매도 상황 ${groupIndex + 1}`}</div>
                        <div class="text-xs text-gray-500">조건을 {displayLogic(group.logic)} • {group.rules.length}개</div>
                      </button>
                      <div class="flex gap-2">
                        <button on:click={() => { selectNode({ type: 'exitGroup', groupIndex }); addRuleToExitGroup({}); }} class="rounded bg-gray-800 px-2 py-1 text-xs text-white">+ 매도 조건</button>
                        <button title="위로" on:click={() => moveNode({ type: 'exitGroup', groupIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                        <button title="아래로" on:click={() => moveNode({ type: 'exitGroup', groupIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                        <button title="복제" on:click={() => duplicateNode({ type: 'exitGroup', groupIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                        <button on:click={() => removeNode({ type: 'exitGroup', groupIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                      </div>
                    </div>
                    <div class="space-y-2 border-l border-gray-800 pl-3">
                      {#each group.rules as rule, ruleIndex}
                        <div class="flex gap-2">
                          <button on:click={() => selectNode({ type: 'exitRule', groupIndex, ruleIndex })} class={`flex-1 rounded border px-3 py-2 text-left text-sm transition ${selectedNode.type === 'exitRule' && selectedNode.groupIndex === groupIndex && selectedNode.ruleIndex === ruleIndex ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-950 text-gray-200 hover:border-gray-700'}`}>
                            {ruleSummary(rule)}
                          </button>
                          <button title="위로" on:click={() => moveNode({ type: 'exitRule', groupIndex, ruleIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                          <button title="아래로" on:click={() => moveNode({ type: 'exitRule', groupIndex, ruleIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                          <button title="복제" on:click={() => duplicateNode({ type: 'exitRule', groupIndex, ruleIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                          <button on:click={() => removeNode({ type: 'exitRule', groupIndex, ruleIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/each}
              </div>
            </div>

            <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
              <div class="mb-4 flex items-center justify-between">
                <button on:click={() => selectNode({ type: 'weightRoot' })} class="text-left">
                  <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                    <span title={tooltipFor('weightTier')} class="cursor-help">얼마나 살까?</span>
                    <span title={tooltipFor('weightTier')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                      <CircleHelp size={12} />
                    </span>
                  </div>
                  <div class="text-lg font-semibold">{workspace.useWeightTiers ? '사용 중' : '사용 안 함'}</div>
                </button>
                <button on:click={() => addNode('weightTier')} class="rounded bg-gray-800 px-3 py-1 text-xs text-white transition hover:bg-gray-700">+ 매수 비중</button>
              </div>
              <div class="space-y-2">
                {#each workspace.weightTiers as tier, tierIndex}
                  <div class="rounded-lg border border-gray-800 bg-gray-900 p-3">
                    <div class="mb-2 flex items-center justify-between">
                      <button on:click={() => selectNode({ type: 'weightTier', tierIndex })} class={`text-left ${selectedNode.type === 'weightTier' && selectedNode.tierIndex === tierIndex ? 'text-blue-300' : 'text-white'}`}>
                        <div class="font-semibold">{tier.label}</div>
                        <div class="text-xs text-gray-500">{displayLogic(tier.logic)} • {tier.allocationPercent}%</div>
                      </button>
                      <div class="flex gap-2">
                        <button on:click={() => addTierCondition(tierIndex)} class="rounded bg-gray-800 px-2 py-1 text-xs text-white">+ 적용 조건</button>
                        <button title="위로" on:click={() => moveNode({ type: 'weightTier', tierIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                        <button title="아래로" on:click={() => moveNode({ type: 'weightTier', tierIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                        <button title="복제" on:click={() => duplicateNode({ type: 'weightTier', tierIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                        <button on:click={() => removeNode({ type: 'weightTier', tierIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                      </div>
                    </div>
                    <div class="space-y-2 border-l border-gray-800 pl-3">
                      {#each tier.conditions as rule, ruleIndex}
                        <div class="flex gap-2">
                          <button on:click={() => selectNode({ type: 'tierRule', tierIndex, ruleIndex })} class={`flex-1 rounded border px-3 py-2 text-left text-sm transition ${selectedNode.type === 'tierRule' && selectedNode.tierIndex === tierIndex && selectedNode.ruleIndex === ruleIndex ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-950 text-gray-200 hover:border-gray-700'}`}>{ruleSummary(rule)}</button>
                          <button title="위로" on:click={() => moveNode({ type: 'tierRule', tierIndex, ruleIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                          <button title="아래로" on:click={() => moveNode({ type: 'tierRule', tierIndex, ruleIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                          <button title="복제" on:click={() => duplicateNode({ type: 'tierRule', tierIndex, ruleIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                          <button on:click={() => removeNode({ type: 'tierRule', tierIndex, ruleIndex })} class="rounded p-1 text-red-400 hover:bg-red-950/30"><Trash2 size={14} /></button>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/each}
              </div>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-5">
            <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
              <div class="mb-4 flex items-center justify-between">
                <button on:click={() => selectNode({ type: 'scalingRoot' })} class="text-left">
                  <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                    <span title={tooltipFor('scalingRule')} class="cursor-help">추가 매수·분할 매도</span>
                    <span title={tooltipFor('scalingRule')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                      <CircleHelp size={12} />
                    </span>
                  </div>
                  <div class="text-lg font-semibold">설정 {workspace.scalingRules.length}개</div>
                </button>
                <button on:click={() => addNode('scalingRule')} class="rounded bg-gray-800 px-3 py-1 text-xs text-white transition hover:bg-gray-700">+ 추가 매수·매도</button>
              </div>
              <div class="space-y-2">
                {#each workspace.scalingRules as rule, scalingIndex}
                  <div class="rounded-lg border border-gray-800 bg-gray-900 p-3">
                    <div class="mb-2 flex items-center justify-between">
                      <button on:click={() => selectNode({ type: 'scalingRule', scalingIndex })} class={`text-left ${selectedNode.type === 'scalingRule' && selectedNode.scalingIndex === scalingIndex ? 'text-blue-300' : 'text-white'}`}>
                        <div class="font-semibold">{displayScalingDirection(rule.direction)}</div>
                        <div class="text-xs text-gray-500">{displayLogic(rule.logic)} • {rule.percent}% • 최대 {rule.maxCount}회</div>
                      </button>
                      <div class="flex gap-2">
                        <button on:click={() => addScalingCondition(scalingIndex)} class="rounded bg-gray-800 px-2 py-1 text-xs text-white">+ 실행 조건</button>
                        <button title="위로" on:click={() => moveNode({ type: 'scalingRule', scalingIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                        <button title="아래로" on:click={() => moveNode({ type: 'scalingRule', scalingIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                        <button title="복제" on:click={() => duplicateNode({ type: 'scalingRule', scalingIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                        <button on:click={() => removeNode({ type: 'scalingRule', scalingIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                      </div>
                    </div>
                    <div class="space-y-2 border-l border-gray-800 pl-3">
                      {#each rule.conditions as condition, ruleIndex}
                        <div class="flex gap-2">
                          <button on:click={() => selectNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex })} class={`flex-1 rounded border px-3 py-2 text-left text-sm transition ${selectedNode.type === 'scalingRuleCondition' && selectedNode.scalingIndex === scalingIndex && selectedNode.ruleIndex === ruleIndex ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-950 text-gray-200 hover:border-gray-700'}`}>{ruleSummary(condition)}</button>
                          <button title="위로" on:click={() => moveNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                          <button title="아래로" on:click={() => moveNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                          <button title="복제" on:click={() => duplicateNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                          <button on:click={() => removeNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex })} class="rounded p-1 text-red-400 hover:bg-red-950/30"><Trash2 size={14} /></button>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/each}
              </div>
            </div>

            <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
              <div class="mb-4 flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                <span title={tooltipFor('runtime')} class="cursor-help">거래 제한·안전장치</span>
                <span title={tooltipFor('runtime')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                  <CircleHelp size={12} />
                </span>
              </div>
              <div class="space-y-2">
                <button on:click={() => selectNode({ type: 'timeFilter' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'timeFilter' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>매매 가능 시기</button>
                <button on:click={() => selectNode({ type: 'circuitBreaker' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'circuitBreaker' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>손실 시 거래 중단</button>
                <button on:click={() => selectNode({ type: 'reentry' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'reentry' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>다시 매수하기까지 대기</button>
                <button on:click={() => selectNode({ type: 'portfolioRules' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'portfolioRules' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>보유 종목·비중 한도</button>
                <button on:click={() => selectNode({ type: 'dynamicExit' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'dynamicExit' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>손절·목표가 계산법</button>
              </div>
            </div>
          </div>
        </div>
      </div>
    {/if}
  </section>

  <aside class="w-[30rem] shrink-0 overflow-y-auto bg-gray-950 p-6">
    {#if !workspace}
      <div class="text-gray-400">전략을 선택하면 세부 설정이 열립니다.</div>
    {:else if selectedNode.type === 'general' || selectedNode.type === 'entryRoot' || selectedNode.type === 'exitRoot' || selectedNode.type === 'weightRoot' || selectedNode.type === 'scalingRoot'}
      <div class="space-y-5">
        <div>
          <div class="mb-2 flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
            <span title={tooltipFor('pattern')} class="cursor-help">전략 세부 설정</span>
            <span title={tooltipFor('pattern')} class="cursor-help text-gray-600 transition hover:text-blue-300">
              <CircleHelp size={12} />
            </span>
          </div>
          <input bind:value={workspace.name} on:input={touch} placeholder="전략 이름" class="mb-3 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
          <textarea bind:value={workspace.description} on:input={touch} rows="3" placeholder="전략 설명" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white"></textarea>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div title={tooltipFor('entryMode')} class="mb-2 cursor-help text-gray-500">언제 주문할까요?</div>
            <select bind:value={workspace.entryMode} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each entryModeOptions as option}<option value={option}>{displayEntryMode(option)}</option>{/each}
            </select>
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div title={tooltipFor('sizingMode')} class="mb-2 cursor-help text-gray-500">주문 금액 계산법</div>
            <select bind:value={workspace.sizingMode} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each sizingModeOptions as option}<option value={option}>{displaySizingMode(option)}</option>{/each}
            </select>
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">매수 상황이 여러 개라면</div>
            <select bind:value={workspace.entryGroupsLogic} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
            </select>
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">매도 조건이 여러 개라면</div>
            <select bind:value={workspace.exitGroupsLogic} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
            </select>
          </label>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">ATR 손절 배수</div>
            <input type="number" step="0.1" bind:value={workspace.atrStopMultiplier} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">ATR 목표 배수</div>
            <input type="number" step="0.1" bind:value={workspace.atrTargetMultiplier} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">최대 보유 봉 수</div>
            <input type="number" bind:value={workspace.maxHoldingBars} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">기본 비중 %</div>
            <input type="number" step="1" bind:value={workspace.defaultAllocationPercent} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">트레일링 ATR</div>
            <input type="number" step="0.1" bind:value={workspace.trailingAtr} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">부분 익절 R</div>
            <input type="number" step="0.1" bind:value={workspace.partialProfitR} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
        </div>

        <label class="flex items-center gap-3 rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
          <input type="checkbox" bind:checked={workspace.requireBullRegime} on:change={touch} />
          강세장일 때만 매수
        </label>
        <label class="flex items-center gap-3 rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
          <input type="checkbox" bind:checked={workspace.useWeightTiers} on:change={touch} />
          상황별 매수 비중 사용
        </label>
        <label class="flex items-center gap-3 rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
          <input type="checkbox" bind:checked={workspace.isActive} on:change={touch} />
          연구·미리보기·백테스트에서 이 전략 사용
        </label>
        <label class="block rounded border border-amber-900/60 bg-amber-950/20 p-3 text-sm text-gray-300">
          <span class="flex items-center gap-3">
            <input type="checkbox" bind:checked={workspace.enableLiveTrading} on:change={touch} />
            실시간 감시와 자동 주문에 연결
          </span>
          <span class="mt-2 block text-xs leading-5 text-amber-300/80">현재 실시간 실행은 관심종목을 일봉으로 판단합니다. 켜면 실제 주문 설정에 따라 주문이 발생할 수 있습니다. 추가 매수·부분 매도 표식은 미리보기와 백테스트에 적용되며, 실시간 브로커는 전량 청산만 지원합니다.</span>
        </label>
      </div>
    {:else if selectedNode.type === 'group'}
      {@const group = workspace.entryGroups[selectedNode.groupIndex]}
      <div class="space-y-4">
        <div title={tooltipFor('entryGroup')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">매수 상황</div>
        <input bind:value={group.label} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        <select bind:value={group.logic} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
          {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
        </select>
        <button on:click={() => addRuleToGroup({})} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">+ 매수 조건 추가</button>
      </div>
    {:else if selectedNode.type === 'exitGroup'}
      {@const group = workspace.exitGroups[selectedNode.groupIndex]}
      <div class="space-y-4">
        <div title={tooltipFor('exitRule')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">매도 상황</div>
        <input bind:value={group.label} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        <label class="block text-sm text-gray-300">
          <div class="mb-2 text-gray-500">이 상황의 조건이 여러 개라면</div>
          <select bind:value={group.logic} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
          </select>
        </label>
        <button on:click={() => addRuleToExitGroup({})} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">+ 매도 조건 추가</button>
      </div>
    {:else if selectedNode.type === 'weightTier'}
      {@const tier = workspace.weightTiers[selectedNode.tierIndex]}
      <div class="space-y-4">
        <div title={tooltipFor('weightTier')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">매수 비중</div>
        <input bind:value={tier.label} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        <div class="grid grid-cols-2 gap-3">
          <select bind:value={tier.logic} on:change={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
          </select>
          <input type="number" bind:value={tier.allocationPercent} on:input={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </div>
        <button on:click={() => addTierCondition(selectedNode.tierIndex)} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">+ 적용 조건 추가</button>
      </div>
    {:else if selectedNode.type === 'scalingRule'}
      {@const rule = workspace.scalingRules[selectedNode.scalingIndex]}
      <div class="space-y-4">
        <div title={tooltipFor('scalingRule')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">추가 매수·분할 매도</div>
        <div class="grid grid-cols-2 gap-3">
          <select bind:value={rule.direction} on:change={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each scalingDirectionOptions as option}<option value={option}>{displayScalingDirection(option)}</option>{/each}
          </select>
          <select bind:value={rule.logic} on:change={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
          </select>
          <input type="number" bind:value={rule.percent} on:input={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="비율 %" />
          <input type="number" bind:value={rule.maxCount} on:input={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="최대 횟수" />
          <input type="number" step="0.1" bind:value={rule.minProfitPercent} on:input={touch} class="col-span-2 rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="최소 수익률 %" />
        </div>
        <button on:click={() => addScalingCondition(selectedNode.scalingIndex)} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">+ 실행 조건 추가</button>
      </div>
    {:else if selectedNode.type === 'timeFilter'}
      <div class="space-y-4">
        <div class="text-xs uppercase tracking-wider text-gray-500">매매 가능 시기</div>
        <label class="block text-sm text-gray-300">
          <div class="mb-2 text-gray-500">허용 요일 (0~6)</div>
          <input value={listToText(workspace.timeFilter.allowedDaysOfWeek)} on:input={(e) => { workspace.timeFilter.allowedDaysOfWeek = textToIntList(e.currentTarget.value); touch(); }} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="block text-sm text-gray-300">
          <div class="mb-2 text-gray-500">차단 월 (1~12)</div>
          <input value={listToText(workspace.timeFilter.blockedMonths)} on:input={(e) => { workspace.timeFilter.blockedMonths = textToIntList(e.currentTarget.value); touch(); }} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
      </div>
    {:else if selectedNode.type === 'circuitBreaker'}
      <div class="space-y-4">
        <div class="text-xs uppercase tracking-wider text-gray-500">손실 시 거래 중단</div>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">연속 손실 허용 횟수</span><input type="number" bind:value={workspace.circuitBreaker.consecutiveLossLimit} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">거래를 멈출 봉 수</span><input type="number" bind:value={workspace.circuitBreaker.cooldownBars} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">전략 최대 낙폭 %</span><input type="number" step="0.1" bind:value={workspace.circuitBreaker.maxDrawdownPercent} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
      </div>
    {:else if selectedNode.type === 'reentry'}
      <div class="space-y-4">
        <div class="text-xs uppercase tracking-wider text-gray-500">다시 매수하기까지 대기</div>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">손실 후 대기 봉 수</span><input type="number" bind:value={workspace.reentry.cooldownBarsAfterLoss} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">수익 후 대기 봉 수</span><input type="number" bind:value={workspace.reentry.cooldownBarsAfterWin} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
      </div>
    {:else if selectedNode.type === 'portfolioRules'}
      <div class="space-y-4">
        <div class="text-xs uppercase tracking-wider text-gray-500">보유 종목·비중 한도</div>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">동시에 보유할 최대 종목 수</span><input type="number" bind:value={workspace.portfolioRules.maxTotalPositions} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">한 종목의 최대 비중 %</span><input type="number" step="0.1" bind:value={workspace.portfolioRules.maxSinglePositionPercent} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">하루 최대 매수 횟수</span><input type="number" bind:value={workspace.portfolioRules.maxEntriesPerDay} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">최대 상관계수 (백테스트)</span><input type="number" step="0.01" bind:value={workspace.portfolioRules.maxCorrelation} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
      </div>
    {:else if selectedNode.type === 'dynamicExit'}
      <div class="space-y-4">
        <div title={tooltipFor('dynamicExit')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">손절·목표가 계산법</div>
        <div class="rounded border border-gray-800 bg-gray-900 p-4">
          <div class="mb-3 text-sm font-semibold text-white">손절</div>
          <select bind:value={workspace.dynamicExit.stopType} on:change={(e) => setDynamicExitType('stop', e.currentTarget.value)} class="mb-3 w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
            {#each stopTypeOptions as option}<option value={option}>{displayStopType(option)}</option>{/each}
          </select>
          <div class="grid grid-cols-2 gap-3">
            {#each getDynamicFieldConfigs('stop', workspace.dynamicExit.stopType) as field}
              <label class="block text-sm text-gray-300">
                <div class="mb-2 text-gray-500">{field.label}</div>
                <input type="number" step={field.step} value={workspace.dynamicExit.stopParams[field.key] ?? field.defaultValue} on:input={(e) => updateDynamicParam('stop', field.key, e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
              </label>
            {/each}
          </div>
        </div>
        <div class="rounded border border-gray-800 bg-gray-900 p-4">
          <div class="mb-3 text-sm font-semibold text-white">목표가</div>
          <select bind:value={workspace.dynamicExit.targetType} on:change={(e) => setDynamicExitType('target', e.currentTarget.value)} class="mb-3 w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
            {#each targetTypeOptions as option}<option value={option}>{displayTargetType(option)}</option>{/each}
          </select>
          <div class="grid grid-cols-2 gap-3">
            {#each getDynamicFieldConfigs('target', workspace.dynamicExit.targetType) as field}
              <label class="block text-sm text-gray-300">
                <div class="mb-2 text-gray-500">{field.label}</div>
                <input type="number" step={field.step} value={workspace.dynamicExit.targetParams[field.key] ?? field.defaultValue} on:input={(e) => updateDynamicParam('target', field.key, e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
              </label>
            {/each}
          </div>
        </div>
      </div>
    {:else}
      {@const rule = getCurrentRule()}
      {#if rule}
        <div class="space-y-4">
          <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
            <span title={tooltipFor('ruleInspector')} class="cursor-help">선택한 조건 바꾸기</span>
            <span title={tooltipFor('ruleInspector')} class="cursor-help text-gray-600 transition hover:text-blue-300">
              <CircleHelp size={12} />
            </span>
          </div>
          <select bind:value={rule.indicator} on:change={(e) => updateRuleField('indicator', e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each indicatorPalette as section}
              <optgroup label={section.title}>
                {#each section.items as item}
                  <option value={item.indicator}>{item.label}</option>
                {/each}
              </optgroup>
            {/each}
          </select>
          <div class="grid grid-cols-2 gap-3">
            <select bind:value={rule.operator} on:change={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
              {#each operatorOptions as option}<option value={option}>{operatorLabels[option] ?? option}</option>{/each}
            </select>
            <input type="number" step="0.1" bind:value={rule.value} on:input={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="기준값" />
            <input type="number" bind:value={rule.withinBars} on:input={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="최근 N봉 내" />
            <input type="number" bind:value={rule.consecutiveBars} on:input={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="연속 봉 수" />
            <input type="number" step="0.1" bind:value={rule.weight} on:input={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="가중치" />
            <input bind:value={rule.refSymbol} on:input={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="참조 심볼" />
          </div>
          <div class="rounded border border-gray-800 bg-gray-900 p-3 text-xs text-gray-400">
            조건이 최근에 한 번이라도 나왔는지는 <span class="text-gray-200">최근 N봉 내</span>, 계속 이어져야 한다면 <span class="text-gray-200">연속 봉 수</span>를 사용하세요. 두 값은 하나만 쓰는 것이 좋습니다.
          </div>
          <select bind:value={rule.compareIndicator} on:change={(e) => updateRuleField('compareIndicator', e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            <option value="">고정값과 비교</option>
            {#each indicatorPalette as section}
              <optgroup label={section.title}>
                {#each section.items as item}
                  <option value={item.indicator}>{item.label}</option>
                {/each}
              </optgroup>
            {/each}
          </select>

          <div class="rounded border border-gray-800 bg-gray-900 p-4">
            <div class="mb-2 flex items-center justify-between">
              <div class="text-sm font-semibold text-white">지표 계산 설정</div>
              <button on:click={() => addRuleMapEntry('params')} class="rounded bg-gray-800 px-2 py-1 text-xs text-white">+ 고급 계산값</button>
            </div>
            {#if getIndicatorFieldConfigs(rule.indicator).length > 0}
              <div class="mb-3 grid grid-cols-2 gap-3">
                {#each getIndicatorFieldConfigs(rule.indicator) as field}
                  <label class="block text-sm text-gray-300">
                    <div class="mb-2 text-gray-500">{field.label}</div>
                    <input type="number" step={field.step} value={rule.params?.[field.key] ?? field.defaultValue} on:input={(e) => updateRuleMapEntry('params', field.key, field.key, e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  </label>
                {/each}
              </div>
            {/if}
            <div class="space-y-2">
              {#each getExtraParamEntries(rule.params, rule.indicator) as [key, value]}
                <div class="grid grid-cols-[1fr,1fr,auto] gap-2">
                  <input value={key} on:input={(e) => updateRuleMapEntry('params', key, e.currentTarget.value, value)} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  <input type="number" step="0.1" value={value} on:input={(e) => updateRuleMapEntry('params', key, key, e.currentTarget.value)} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  <button on:click={() => removeRuleMapEntry('params', key)} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                </div>
              {/each}
            </div>
          </div>

          <div class="rounded border border-gray-800 bg-gray-900 p-4">
            <div class="mb-2 flex items-center justify-between">
              <div class="text-sm font-semibold text-white">비교 지표 계산 설정</div>
              <button on:click={() => addRuleMapEntry('compareParams')} disabled={!rule.compareIndicator} class="rounded bg-gray-800 px-2 py-1 text-xs text-white disabled:opacity-40">+ 고급 계산값</button>
            </div>
            {#if rule.compareIndicator && getIndicatorFieldConfigs(rule.compareIndicator).length > 0}
              <div class="mb-3 grid grid-cols-2 gap-3">
                {#each getIndicatorFieldConfigs(rule.compareIndicator) as field}
                  <label class="block text-sm text-gray-300">
                    <div class="mb-2 text-gray-500">{field.label}</div>
                    <input type="number" step={field.step} value={rule.compareParams?.[field.key] ?? field.defaultValue} on:input={(e) => updateRuleMapEntry('compareParams', field.key, field.key, e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  </label>
                {/each}
              </div>
            {/if}
            <div class="space-y-2">
              {#each getExtraParamEntries(rule.compareParams, rule.compareIndicator) as [key, value]}
                <div class="grid grid-cols-[1fr,1fr,auto] gap-2">
                  <input value={key} on:input={(e) => updateRuleMapEntry('compareParams', key, e.currentTarget.value, value)} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  <input type="number" step="0.1" value={value} on:input={(e) => updateRuleMapEntry('compareParams', key, key, e.currentTarget.value)} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  <button on:click={() => removeRuleMapEntry('compareParams', key)} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                </div>
              {/each}
            </div>
            {#if !rule.compareIndicator}
              <div class="text-xs text-gray-500">비교 지표를 선택하면 해당 지표의 계산 설정이 여기 표시됩니다.</div>
            {/if}
          </div>
        </div>
      {/if}
    {/if}
  </aside>
</div>

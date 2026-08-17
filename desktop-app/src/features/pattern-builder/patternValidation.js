function toNumber(value, fallback = 0) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

export function collectPatternValidationIssues(currentWorkspace, context = {}) {
  if (!currentWorkspace) return []

  const indicatorSet = context.indicatorSet ?? new Set()
  const positiveParamKeys = context.positiveParamKeys ?? new Set()
  const paramKeyLabels = context.paramKeyLabels ?? {}
  const liveStrategyConstraints = context.liveStrategyConstraints ?? null
  const issues = []
  const checkRule = (rule, scope) => {
    const indicator = (rule.indicator ?? '').trim().toUpperCase()
    const compareIndicator = (rule.compareIndicator ?? '').trim().toUpperCase()
    if (!indicatorSet.has(indicator)) issues.push(`${scope}: 지원하지 않는 지표 ${rule.indicator || '(비어 있음)'}`)
    if (compareIndicator && !indicatorSet.has(compareIndicator)) issues.push(`${scope}: 지원하지 않는 비교 지표입니다.`)
    if (toNumber(rule.withinBars, 0) > 0 && toNumber(rule.consecutiveBars, 0) > 0) issues.push(`${scope}: 최근 N봉 내와 연속 봉 수는 동시에 사용할 수 없습니다.`)
    if (toNumber(rule.withinBars, 0) < 0 || toNumber(rule.consecutiveBars, 0) < 0) issues.push(`${scope}: 봉 수는 0 이상이어야 합니다.`)
    if (toNumber(rule.weight, 0) <= 0) issues.push(`${scope}: 가중치는 0보다 커야 합니다.`)
    if (Object.values(rule.params ?? {}).some((value) => toNumber(value, -1) < 0)) issues.push(`${scope}: 지표 계산값은 음수일 수 없습니다.`)
    for (const [key, value] of Object.entries(rule.params ?? {})) {
      if (positiveParamKeys.has(key) && toNumber(value, 0) <= 0) issues.push(`${scope}: ${paramKeyLabels[key] ?? key}은 0보다 커야 합니다.`)
    }
    for (const [key, value] of Object.entries(rule.compareParams ?? {})) {
      if (positiveParamKeys.has(key) && toNumber(value, 0) <= 0) issues.push(`${scope}: 비교 지표의 ${paramKeyLabels[key] ?? key}은 0보다 커야 합니다.`)
    }
    if (indicator === 'MACD_HIST' && toNumber(rule.params?.fast, 12) >= toNumber(rule.params?.slow, 26)) issues.push(`${scope}: 빠른 EMA는 느린 EMA보다 작아야 합니다.`)
  }

  if (!currentWorkspace.name.trim()) issues.push('전략 이름을 입력하세요.')
  if (toNumber(currentWorkspace.atrStopMultiplier, 0) <= 0) issues.push('ATR 손절 배수는 0보다 커야 합니다.')
  if (toNumber(currentWorkspace.atrTargetMultiplier, 0) <= 0) issues.push('ATR 목표 배수는 0보다 커야 합니다.')
  if (toNumber(currentWorkspace.maxHoldingBars, -1) < 0) issues.push('최대 보유 봉 수는 0 이상이어야 합니다.')
  if (toNumber(currentWorkspace.trailingAtr, -1) < 0 || toNumber(currentWorkspace.partialProfitR, -1) < 0) issues.push('트레일링 ATR과 부분 익절 R은 0 이상이어야 합니다.')
  if (toNumber(currentWorkspace.defaultAllocationPercent, -1) < 0 || toNumber(currentWorkspace.defaultAllocationPercent, 101) > 100) issues.push('기본 매수 비중은 0~100%여야 합니다.')

  if (!currentWorkspace.entryGroups.length) issues.push('매수 조건 묶음이 최소 1개는 필요합니다.')
  currentWorkspace.entryGroups.forEach((group, groupIndex) => {
    if (!group.rules.length) issues.push(`매수 상황 ${groupIndex + 1}: 조건이 비어 있습니다.`)
    group.rules.forEach((rule, ruleIndex) => checkRule(rule, `매수 상황 ${groupIndex + 1} / 조건 ${ruleIndex + 1}`))
  })
  currentWorkspace.exitGroups.forEach((group, groupIndex) => {
    if (!group.rules.length) issues.push(`매도 상황 ${groupIndex + 1}: 조건이 비어 있습니다.`)
    group.rules.forEach((rule, ruleIndex) => checkRule(rule, `매도 상황 ${groupIndex + 1} / 조건 ${ruleIndex + 1}`))
  })

  if (currentWorkspace.useWeightTiers) currentWorkspace.weightTiers.forEach((tier, tierIndex) => {
    if (toNumber(tier.allocationPercent, -1) < 0 || toNumber(tier.allocationPercent, 101) > 100) issues.push(`매수 비중 ${tierIndex + 1}: 비중은 0~100%여야 합니다.`)
    if (!tier.conditions.length) issues.push(`매수 비중 ${tierIndex + 1}: 조건이 비어 있습니다.`)
    tier.conditions.forEach((rule, ruleIndex) => checkRule(rule, `매수 비중 ${tierIndex + 1} / 조건 ${ruleIndex + 1}`))
  })

  currentWorkspace.scalingRules.forEach((scalingRule, scalingIndex) => {
    if (toNumber(scalingRule.percent, 0) <= 0 || toNumber(scalingRule.percent, 101) > 100) issues.push(`추가 매수·분할 매도 ${scalingIndex + 1}: 비율은 0 초과 100% 이하여야 합니다.`)
    if (toNumber(scalingRule.maxCount, 0) < 1) issues.push(`추가 매수·분할 매도 ${scalingIndex + 1}: 최대 횟수는 1 이상이어야 합니다.`)
    if (!scalingRule.conditions.length) issues.push(`추가 매수·분할 매도 ${scalingIndex + 1}: 조건이 비어 있습니다.`)
    scalingRule.conditions.forEach((rule, ruleIndex) => checkRule(rule, `추가 매수·분할 매도 ${scalingIndex + 1} / 조건 ${ruleIndex + 1}`))
  })

  if (currentWorkspace.circuitBreaker.consecutiveLossLimit < 0 || currentWorkspace.circuitBreaker.cooldownBars < 0) issues.push('손실 횟수와 거래 중단 봉 수는 0 이상이어야 합니다.')
  if (currentWorkspace.circuitBreaker.maxDrawdownPercent < 0 || currentWorkspace.circuitBreaker.maxDrawdownPercent > 100) issues.push('최대 낙폭은 0~100%여야 합니다.')
  if (currentWorkspace.enableLiveTrading && liveStrategyConstraints) {
    if (!liveStrategyConstraints.supportedTimeFrames.includes(currentWorkspace.timeFrame)) issues.push('선택한 시간축은 실시간 주문에서 아직 지원하지 않습니다.')
    if (!liveStrategyConstraints.supportedEntryModes.includes(currentWorkspace.entryMode)) issues.push('선택한 매수 체결 시점은 실시간 주문에서 아직 지원하지 않습니다.')
    if (!liveStrategyConstraints.supportsPartialExit && currentWorkspace.partialProfitR > 0) issues.push('부분 익절 전략은 실시간 주문을 아직 켤 수 없습니다.')
    if (!liveStrategyConstraints.supportsScaling && currentWorkspace.scalingRules.length > 0) issues.push('추가 매수·분할 매도 전략은 실시간 주문을 아직 켤 수 없습니다.')
  }
  if (currentWorkspace.reentry.cooldownBarsAfterLoss < 0 || currentWorkspace.reentry.cooldownBarsAfterWin < 0) issues.push('재매수 대기 봉 수는 0 이상이어야 합니다.')
  if (currentWorkspace.portfolioRules.maxTotalPositions < 0 || currentWorkspace.portfolioRules.maxEntriesPerDay < 0) issues.push('보유 종목 수와 하루 매수 횟수는 0 이상이어야 합니다.')
  if (currentWorkspace.portfolioRules.maxSinglePositionPercent < 0 || currentWorkspace.portfolioRules.maxSinglePositionPercent > 100) issues.push('한 종목 최대 비중은 0~100%여야 합니다.')
  if (currentWorkspace.portfolioRules.maxCorrelation < 0 || currentWorkspace.portfolioRules.maxCorrelation > 1) issues.push('최대 상관계수는 0~1 사이여야 합니다.')

  return issues
}

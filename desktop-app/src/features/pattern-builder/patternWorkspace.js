function safeParse(value, fallback) {
  try {
    return value ? JSON.parse(value) : fallback
  } catch {
    return fallback
  }
}

export function createPatternWorkspaceModel(initialCatalog = {}) {
  let catalog = {
    indicatorFieldConfigs: {},
    dynamicExitFieldConfigs: { stop: {}, target: {} },
    ...initialCatalog
  }

  function configure(nextCatalog = {}) {
    catalog = { ...catalog, ...nextCatalog }
  }

  function toNumber(value, fallback = 0) {
    const parsed = Number(value)
    return Number.isFinite(parsed) ? parsed : fallback
  }

  function sanitizeNumericMap(map = {}) {
    return Object.fromEntries(
      Object.entries(map)
        .map(([key, value]) => [key.trim(), toNumber(value, 0)])
        .filter(([key]) => key.length > 0)
    )
  }

  function normalizeRuleParams(indicator, params = {}) {
    const normalized = { ...params }
    const upper = (indicator ?? '').toUpperCase()
    if (normalized.stdDev != null && normalized.stddev == null) normalized.stddev = normalized.stdDev
    delete normalized.stdDev
    if (['BREAKOUT_HIGH', 'BREAKOUT_LOW', 'DIST_FROM_HIGH', 'DIST_FROM_LOW'].includes(upper) && normalized.lookback != null && normalized.period == null) normalized.period = normalized.lookback
    delete normalized.lookback
    return normalized
  }

  function getIndicatorFieldConfigs(indicator) {
    return catalog.indicatorFieldConfigs[(indicator ?? '').toUpperCase()] ?? []
  }

  function buildRuleParams(indicator, params = {}, applyDefaults = true) {
    const normalized = normalizeRuleParams(indicator, sanitizeNumericMap(params))
    const configs = getIndicatorFieldConfigs(indicator)
    const next = { ...normalized }
    if (applyDefaults) {
      for (const field of configs) {
        if (next[field.key] == null) next[field.key] = field.defaultValue
      }
    }
    return next
  }

  function getExtraParamEntries(paramMap = {}, indicator) {
    const knownKeys = new Set(getIndicatorFieldConfigs(indicator).map((field) => field.key))
    return Object.entries(paramMap || {}).filter(([key]) => !knownKeys.has(key))
  }

  function getDynamicFieldConfigs(kind, type) {
    return catalog.dynamicExitFieldConfigs[kind]?.[type] ?? []
  }

  function normalizeDynamicParams(kind, type, params = {}) {
    const incoming = sanitizeNumericMap(params)
    const configs = getDynamicFieldConfigs(kind, type)
    const normalized = {}
    if (incoming.value != null && configs.length > 0 && configs.every((field) => incoming[field.key] == null)) normalized[configs[0].key] = incoming.value
    for (const field of configs) normalized[field.key] = toNumber(incoming[field.key] ?? normalized[field.key] ?? field.defaultValue, field.defaultValue)
    return normalized
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
    return { label: label ?? '매수 상황', logic: 'AND', rules: [blankRule()] }
  }

  function blankExitGroup(label) {
    return { label: label ?? '매도 상황', logic: 'AND', rules: [blankRule({ indicator: 'RSI', operator: '>=', value: 70, params: { period: 14 } })] }
  }

  function blankWeightTier() {
    return { label: '기본 매수 비중', logic: 'AND', allocationPercent: 100, conditions: [blankRule()] }
  }

  function blankScalingRule() {
    return { direction: 'SCALE_IN', logic: 'AND', percent: 50, maxCount: 1, minProfitPercent: 0, conditions: [blankRule()] }
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
    return { label: group.label ?? group.Label ?? '매수 상황', logic: group.logic ?? group.Logic ?? 'AND', rules: (group.rules ?? group.Rules ?? []).map(normalizeRule) }
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
    const timeFilter = safeParse(raw.timeFilterJson, {})
    const circuitBreaker = safeParse(raw.circuitBreakerJson, {})
    const reentry = safeParse(raw.reentryJson, {})
    const portfolioRules = safeParse(raw.portfolioRulesJson, {})
    const dynamicExit = safeParse(raw.dynamicExitJson, {})

    return {
      raw,
      name: raw.name ?? '',
      description: raw.description ?? '',
      isActive: raw.isActive ?? true,
      enableLiveTrading: raw.enableLiveTrading ?? false,
      requireBullRegime: !!raw.requireBullRegime,
      entryMode: raw.entryMode ?? 'CurrentClose',
      timeFrame: raw.timeFrame ?? 'Daily',
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
      exitGroups: storedExitGroups.length > 0 ? storedExitGroups : (flatExitRules.length > 0 ? [{ label: '매도 상황 1', logic: raw.exitRulesLogic ?? 'OR', rules: flatExitRules }] : []),
      weightTiers: safeParse(raw.weightTiersJson, []).map(normalizeWeightTier),
      scalingRules: safeParse(raw.scalingRulesJson, []).map(normalizeScalingRule),
      timeFilter: { allowedDaysOfWeek: timeFilter.allowedDaysOfWeek ?? [], blockedMonths: timeFilter.blockedMonths ?? [] },
      circuitBreaker: {
        consecutiveLossLimit: Number(circuitBreaker.consecutiveLossLimit ?? 0),
        cooldownBars: Number(circuitBreaker.cooldownBars ?? 5),
        maxDrawdownPercent: Number(circuitBreaker.maxDrawdownPercent ?? 0)
      },
      reentry: {
        cooldownBarsAfterLoss: Number(reentry.cooldownBarsAfterLoss ?? 0),
        cooldownBarsAfterWin: Number(reentry.cooldownBarsAfterWin ?? 0)
      },
      portfolioRules: {
        maxTotalPositions: Number(portfolioRules.maxTotalPositions ?? 0),
        maxSinglePositionPercent: Number(portfolioRules.maxSinglePositionPercent ?? 0),
        maxEntriesPerDay: Number(portfolioRules.maxEntriesPerDay ?? 0),
        maxCorrelation: Number(portfolioRules.maxCorrelation ?? 0)
      },
      dynamicExit: {
        stopType: dynamicExit.stopType ?? 'ATR',
        stopParams: normalizeDynamicParams('stop', dynamicExit.stopType ?? 'ATR', dynamicExit.stopParams ?? {}),
        targetType: dynamicExit.targetType ?? 'ATR',
        targetParams: normalizeDynamicParams('target', dynamicExit.targetType ?? 'ATR', dynamicExit.targetParams ?? {})
      }
    }
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
    const entryGroups = currentWorkspace.entryGroups.map((group) => ({ ...group, rules: group.rules.map(sanitizeRule) }))
    const exitGroups = currentWorkspace.exitGroups.map((group) => ({ ...group, rules: group.rules.map(sanitizeRule) }))
    const weightTiers = currentWorkspace.weightTiers.map((tier) => ({ ...tier, allocationPercent: toNumber(tier.allocationPercent, 100), conditions: tier.conditions.map(sanitizeRule) }))
    const scalingRules = currentWorkspace.scalingRules.map((rule) => ({ ...rule, percent: toNumber(rule.percent, 50), maxCount: toNumber(rule.maxCount, 1), minProfitPercent: toNumber(rule.minProfitPercent, 0), conditions: rule.conditions.map(sanitizeRule) }))
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
      timeFrame: currentWorkspace.timeFrame,
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

  return {
    configure,
    toNumber,
    sanitizeNumericMap,
    getIndicatorFieldConfigs,
    buildRuleParams,
    getExtraParamEntries,
    getDynamicFieldConfigs,
    normalizeDynamicParams,
    blankRule,
    blankGroup,
    blankExitGroup,
    blankWeightTier,
    blankScalingRule,
    buildWorkspace,
    buildPatternPayload
  }
}

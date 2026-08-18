function options(items) {
  return (items ?? []).map((item) => item.code)
}

function labels(items) {
  return Object.fromEntries((items ?? []).map((item) => [item.code, item.displayName]))
}

function fieldConfigs(items) {
  return Object.fromEntries((items ?? []).map((item) => [
    item.code,
    (item.parameters ?? []).map((parameter) => ({
      key: parameter.key,
      label: parameter.displayName,
      step: String(parameter.step),
      defaultValue: parameter.defaultValue
    }))
  ]))
}

export function emptyPatternMetadata() {
  return {
    indicatorOptions: [],
    indicatorPalette: [],
    indicatorSet: new Set(),
    indicatorLabels: {},
    indicatorValueGuides: {},
    indicatorFieldConfigs: {},
    positiveParamKeys: new Set(['multiplier', 'multiple', 'percent']),
    operatorOptions: [],
    timeFrameOptions: [],
    entryModeOptions: [],
    sizingModeOptions: [],
    logicOptions: [],
    scalingDirectionOptions: [],
    stopTypeOptions: [],
    targetTypeOptions: [],
    entryModeLabels: {},
    sizingModeLabels: {},
    logicLabels: {},
    scalingDirectionLabels: {},
    stopTypeLabels: {},
    targetTypeLabels: {},
    dynamicExitFieldConfigs: { stop: {}, target: {} },
    liveStrategyConstraints: null
  }
}

export function projectPatternMetadata(source) {
  const indicators = source?.indicators ?? []
  const indicatorOptions = indicators.map((item) => ({
    label: item.displayName,
    indicator: item.code,
    operator: item.defaultOperator,
    value: item.defaultThreshold,
    params: Object.fromEntries(
      (item.parameters ?? []).map((parameter) => [parameter.key, parameter.defaultValue])
    )
  }))
  const byCode = new Map(indicatorOptions.map((item) => [item.indicator, item]))
  const grouped = new Map()
  for (const item of indicators) {
    if (!grouped.has(item.category)) grouped.set(item.category, [])
    grouped.get(item.category).push(byCode.get(item.code))
  }

  const projected = {
    indicatorOptions,
    indicatorPalette: [...grouped].map(([title, items]) => ({ title, items })),
    indicatorSet: new Set(indicators.map((item) => item.code)),
    indicatorLabels: Object.fromEntries(indicators.map((item) => [item.code, item.displayName])),
    indicatorValueGuides: Object.fromEntries(
      indicators.filter((item) => item.valueGuide).map((item) => [item.code, item.valueGuide])
    ),
    indicatorFieldConfigs: fieldConfigs(indicators),
    positiveParamKeys: new Set([
      'multiplier', 'multiple', 'percent',
      ...indicators.flatMap((item) =>
        (item.parameters ?? [])
          .filter((parameter) => parameter.mustBePositive)
          .map((parameter) => parameter.key)
      )
    ]),
    operatorOptions: source?.ruleOperators ?? [],
    timeFrameOptions: (source?.timeFrames ?? []).map((item) => ({
      value: item.value,
      label: item.displayName
    })),
    entryModeOptions: options(source?.entryModes),
    sizingModeOptions: options(source?.sizingModes),
    logicOptions: options(source?.logicModes),
    scalingDirectionOptions: options(source?.scalingDirections),
    stopTypeOptions: options(source?.stopMethods),
    targetTypeOptions: options(source?.targetMethods),
    entryModeLabels: labels(source?.entryModes),
    sizingModeLabels: labels(source?.sizingModes),
    logicLabels: labels(source?.logicModes),
    scalingDirectionLabels: labels(source?.scalingDirections),
    stopTypeLabels: labels(source?.stopMethods),
    targetTypeLabels: labels(source?.targetMethods),
    dynamicExitFieldConfigs: {
      stop: fieldConfigs(source?.stopMethods),
      target: fieldConfigs(source?.targetMethods)
    },
    liveStrategyConstraints: source?.liveStrategyConstraints ?? null
  }

  if (!projected.indicatorOptions.length ||
      !projected.timeFrameOptions.length ||
      !projected.operatorOptions.length ||
      !projected.entryModeOptions.length ||
      !projected.stopTypeOptions.length) {
    throw new Error('서버의 전략 구성 메타데이터가 비어 있습니다.')
  }
  return projected
}

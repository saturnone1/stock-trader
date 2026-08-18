function options(items) {
  return (items ?? []).map((item) => ({
    code: item.code,
    label: item.displayName,
    description: item.description ?? '',
    available: item.isAvailable !== false
  }))
}

export function parseWatchlist(value) {
  return String(value ?? '')
    .split(/[\s,]+/)
    .map((item) => item.trim().toUpperCase())
    .filter(Boolean)
    .filter((item, index, all) => all.indexOf(item) === index)
}

export function createSettingsForm(source) {
  const orderModes = options(source?.orderModes)
  const dataProviders = options(source?.dataProviders)
  const patterns = options(source?.patterns)
  if (!orderModes.length || !dataProviders.length || !patterns.length) {
    throw new Error('서버의 설정 선택 정보가 비어 있습니다.')
  }

  const orderMode = source?.orderMode
  const preferredDataSource = source?.preferredDataSource
  if (!orderModes.some((item) => item.code === orderMode) ||
      !dataProviders.some((item) => item.code === preferredDataSource)) {
    throw new Error('저장된 설정이 현재 서버 지원 범위와 맞지 않습니다.')
  }

  return {
    orderMode,
    preferredDataSource,
    enabledPatterns: [...(source?.enabledPatterns ?? [])]
      .filter((code) => patterns.some((item) => item.code === code && item.available)),
    watchlistText: (source?.watchlistSymbols ?? []).join(', '),
    soundAlerts: !!source?.soundAlerts,
    accountSize: source?.accountSize ?? 100000,
    riskPerTradePercent: source?.riskPerTradePercent ?? 0.01,
    dailyLossLimitPercent: source?.dailyLossLimitPercent ?? 0.03,
    maxTotalPositions: source?.maxTotalPositions ?? 7,
    maxPositionsPerSector: source?.maxPositionsPerSector ?? 2,
    minExpectancy: source?.minExpectancy ?? 0,
    orderModes,
    dataProviders,
    patterns
  }
}

export function setPatternEnabled(form, code, enabled) {
  if (!form.patterns.some((item) => item.code === code && item.available)) return form
  const selected = new Set(form.enabledPatterns)
  if (enabled) selected.add(code)
  else selected.delete(code)
  return { ...form, enabledPatterns: [...selected] }
}

export function validateSettingsForm(form) {
  const errors = []
  const symbols = parseWatchlist(form.watchlistText)
  if (symbols.length > 100) errors.push('관심종목은 최대 100개까지 저장할 수 있습니다.')
  if (symbols.some((symbol) => !/^[A-Z0-9][A-Z0-9.-]{0,14}$/.test(symbol))) {
    errors.push('관심종목 코드를 확인해 주세요.')
  }
  if (!(Number(form.accountSize) > 0)) errors.push('계좌 기준 금액을 입력해 주세요.')
  if (!(Number(form.riskPerTradePercent) > 0 && Number(form.riskPerTradePercent) <= 1)) {
    errors.push('거래당 손실 허용률은 0보다 크고 1 이하여야 합니다.')
  }
  if (!(Number(form.dailyLossLimitPercent) > 0 && Number(form.dailyLossLimitPercent) <= 1)) {
    errors.push('일일 손실 한도는 0보다 크고 1 이하여야 합니다.')
  }
  if (!(Number(form.maxTotalPositions) >= 1)) errors.push('전체 최대 보유 수는 1개 이상이어야 합니다.')
  if (!(Number(form.maxPositionsPerSector) >= 1) ||
      Number(form.maxPositionsPerSector) > Number(form.maxTotalPositions)) {
    errors.push('업종별 최대 보유 수를 전체 최대 보유 수 이하로 입력해 주세요.')
  }
  if (Number(form.minExpectancy) < 0) errors.push('최소 기대값은 0 이상이어야 합니다.')
  return errors
}

export function buildSettingsRequest(form) {
  return {
    orderMode: form.orderMode,
    preferredDataSource: form.preferredDataSource,
    enabledPatterns: [...form.enabledPatterns],
    watchlistSymbols: parseWatchlist(form.watchlistText),
    soundAlerts: !!form.soundAlerts,
    accountSize: Number(form.accountSize),
    riskPerTradePercent: Number(form.riskPerTradePercent),
    dailyLossLimitPercent: Number(form.dailyLossLimitPercent),
    maxTotalPositions: Number(form.maxTotalPositions),
    maxPositionsPerSector: Number(form.maxPositionsPerSector),
    minExpectancy: Number(form.minExpectancy)
  }
}

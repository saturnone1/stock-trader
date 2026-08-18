export function normalizeAccountsResponse(payload = {}) {
  const rows = payload.accounts ?? payload.Accounts ?? []
  return Array.isArray(rows) ? rows : []
}

export function brokerOptionsFromMetadata(payload = {}) {
  const options = payload.brokers ?? payload.Brokers ?? []
  return Array.isArray(options) ? options : []
}

export function createAccountForm(options = []) {
  const broker = options.find((item) => item.isImplemented) ?? options[0]
  return {
    accountName: '',
    brokerType: broker?.type ?? 'Alpaca',
    apiKey: '',
    apiSecret: '',
    environment: broker?.defaultEnvironment ?? 'Paper',
    isActive: false,
    isEnabled: true,
    notes: '',
  }
}

export function selectBroker(form, options, brokerType) {
  const broker = options.find((item) => item.type === brokerType)
  return {
    ...form,
    brokerType,
    environment: broker?.defaultEnvironment ?? form.environment,
  }
}

export function projectAccountError(error, fallback) {
  const payload = error?.response?.data
  if (Array.isArray(payload?.errors) && payload.errors.length > 0)
    return payload.errors.join(' ')
  return payload?.error || error?.message || fallback
}

export function tradeApiError(error, fallback) {
  const response = error?.response?.data
  if (Array.isArray(response?.errors) && response.errors.length > 0) {
    return response.errors.join(' ')
  }
  if (response?.errors && typeof response.errors === 'object') {
    const messages = Object.values(response.errors)
      .flatMap((value) => Array.isArray(value) ? value : [value])
      .filter((value) => typeof value === 'string' && value.trim().length > 0)
    if (messages.length > 0) return messages.join(' ')
  }
  return response?.error || response?.title || error?.message || fallback
}

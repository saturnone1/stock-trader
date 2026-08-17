/**
 * Convert the stored-strategy response into the storage-independent document accepted by
 * preview/backtest/optimization. Persistence audit fields must never leak into execution requests.
 */
export function toStrategyDocument(raw) {
  if (!raw || typeof raw !== 'object') return raw
  const { id, createdAt, updatedAt, ...document } = raw
  if (document.storedStrategyId == null && Number(id) > 0) {
    document.storedStrategyId = Number(id)
  }
  return document
}

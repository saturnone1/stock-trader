function hydratedPattern(response, buildWorkspace) {
  const pattern = response?.data
  if (!pattern) throw new Error('서버가 매매 전략 정보를 반환하지 않았습니다.')
  return { pattern, workspace: buildWorkspace(pattern.raw) }
}

export function patternPersistenceError(error, fallback) {
  return error?.response?.data?.error ?? error?.message ?? fallback
}

export function createPatternPersistence({ api, buildWorkspace, buildPatternPayload }) {
  async function list() {
    const response = await api.list()
    return Array.isArray(response?.data) ? response.data : []
  }

  async function create(name) {
    const response = await api.create({ name: name.trim(), description: '' })
    return hydratedPattern(response, buildWorkspace)
  }

  async function open(id) {
    const response = await api.get(id)
    return hydratedPattern(response, buildWorkspace)
  }

  async function save(id, workspace) {
    const response = await api.update(id, buildPatternPayload(workspace))
    return hydratedPattern(response, buildWorkspace)
  }

  async function remove(id) {
    await api.delete(id)
  }

  return { list, create, open, save, remove }
}

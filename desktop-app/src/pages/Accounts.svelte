<script>
  import { onMount } from 'svelte'
  import { accountApi } from '../api/endpoints'

  let loading = true
  let error = ''
  let message = ''
  let rows = []
  let showForm = false
  let form = {
    accountName: '',
    brokerType: 0,
    apiKey: '',
    apiSecret: '',
    environment: 'Paper',
    isActive: false,
    isEnabled: true,
    notes: '',
  }

  onMount(load)

  async function load() {
    loading = true
    try {
      const { data } = await accountApi.list()
      rows = data?.Accounts ?? data?.accounts ?? []
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '계좌 목록을 불러오지 못했습니다.'
    } finally {
      loading = false
    }
  }

  async function create() {
    try {
      await accountApi.create(form)
      showForm = false
      message = '계좌를 추가했습니다.'
      error = ''
      await load()
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '계좌 추가에 실패했습니다.'
    }
  }

  async function activate(id) {
    try {
      await accountApi.activate(id)
      message = '활성 계좌를 변경했습니다.'
      await load()
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '활성화에 실패했습니다.'
    }
  }

  async function testConnection(id) {
    try {
      const { data } = await accountApi.test(id)
      message = data?.StatusMessage ?? data?.statusMessage ?? '연결 테스트 완료'
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '연결 테스트에 실패했습니다.'
    }
  }

  async function remove(id) {
    if (!confirm('이 계좌를 삭제할까요?')) return
    try {
      await accountApi.remove(id)
      message = '계좌를 삭제했습니다.'
      await load()
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '계좌 삭제에 실패했습니다.'
    }
  }
</script>

<div class="flex-1 overflow-auto p-8">
  <div class="flex items-center justify-between mb-8">
    <h2 class="text-4xl font-bold">계좌 관리</h2>
    <div class="flex gap-3">
      <button on:click={load} class="bg-gray-700 hover:bg-gray-600 px-4 py-2 rounded transition text-sm">새로고침</button>
      <button on:click={() => (showForm = !showForm)} class="bg-blue-600 hover:bg-blue-700 px-4 py-2 rounded transition text-sm">{showForm ? '닫기' : '계좌 추가'}</button>
    </div>
  </div>

  {#if message}
    <div class="mb-6 rounded-lg border border-green-700 bg-green-900/20 p-4 text-green-300">{message}</div>
  {/if}
  {#if error}
    <div class="mb-6 rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
  {/if}

  {#if showForm}
    <div class="mb-8 rounded-lg border border-gray-700 bg-gray-800 p-6">
      <div class="grid grid-cols-2 gap-4">
        <input bind:value={form.accountName} placeholder="계좌 이름" class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        <select bind:value={form.environment} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
          <option value="Paper">Paper</option>
          <option value="Live">Live</option>
        </select>
        <input bind:value={form.apiKey} placeholder="API Key" class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        <input bind:value={form.apiSecret} placeholder="API Secret" class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        <textarea bind:value={form.notes} placeholder="메모" class="col-span-2 rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white"></textarea>
      </div>
      <div class="mt-4 flex gap-3">
        <button on:click={create} class="bg-green-600 hover:bg-green-700 px-4 py-2 rounded transition text-sm">저장</button>
      </div>
    </div>
  {/if}

  {#if loading}
    <div class="text-gray-400">불러오는 중...</div>
  {:else}
    <div class="overflow-hidden rounded-lg border border-gray-700 bg-gray-800">
      <table class="w-full text-sm">
        <thead class="bg-gray-900/80 text-gray-400">
          <tr>
            <th class="px-4 py-3 text-left">Name</th>
            <th class="px-4 py-3 text-left">Broker</th>
            <th class="px-4 py-3 text-left">Env</th>
            <th class="px-4 py-3 text-left">API Key</th>
            <th class="px-4 py-3 text-left">Status</th>
            <th class="px-4 py-3 text-right">Actions</th>
          </tr>
        </thead>
        <tbody>
          {#each rows as row}
            <tr class="border-t border-gray-700">
              <td class="px-4 py-3">{row.AccountName}</td>
              <td class="px-4 py-3">{row.BrokerType}</td>
              <td class="px-4 py-3">{row.Environment}</td>
              <td class="px-4 py-3 font-mono text-xs">{row.ApiKey}</td>
              <td class="px-4 py-3">{row.IsActive ? 'Active' : row.IsEnabled ? 'Enabled' : 'Disabled'}</td>
              <td class="px-4 py-3 text-right">
                <div class="flex justify-end gap-2">
                  <button on:click={() => testConnection(row.Id)} class="rounded bg-gray-700 px-3 py-1 text-xs hover:bg-gray-600">Test</button>
                  <button on:click={() => activate(row.Id)} class="rounded bg-blue-700 px-3 py-1 text-xs hover:bg-blue-600">Activate</button>
                  <button on:click={() => remove(row.Id)} class="rounded bg-red-700 px-3 py-1 text-xs hover:bg-red-600">Delete</button>
                </div>
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</div>

<script>
  import { onMount } from 'svelte'
  import { accountApi } from '../api/endpoints'
  import {
    brokerOptionsFromMetadata,
    brokerCapabilityLabels,
    createAccountForm,
    normalizeAccountsResponse,
    projectAccountError,
    selectBroker,
  } from '../features/accounts/accountModel.js'

  let loading = true
  let error = ''
  let message = ''
  let rows = []
  let brokerOptions = []
  let showForm = false
  let form = createAccountForm()
  $: selectedBroker = brokerOptions.find((item) => item.type === form.brokerType)

  function brokerFor(type) {
    return brokerOptions.find((item) => item.type === type)
  }

  onMount(load)

  async function load() {
    loading = true
    try {
      const [{ data }, { data: metadata }] = await Promise.all([
        accountApi.list(),
        accountApi.metadata(),
      ])
      rows = normalizeAccountsResponse(data)
      brokerOptions = brokerOptionsFromMetadata(metadata)
      if (!brokerOptions.some((item) => item.type === form.brokerType))
        form = createAccountForm(brokerOptions)
      error = ''
    } catch (e) {
      error = projectAccountError(e, '계좌 목록을 불러오지 못했습니다.')
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
      error = projectAccountError(e, '계좌 추가에 실패했습니다.')
    }
  }

  async function activate(id) {
    try {
      await accountApi.activate(id)
      message = '활성 계좌를 변경했습니다.'
      await load()
    } catch (e) {
      error = projectAccountError(e, '활성화에 실패했습니다.')
    }
  }

  async function testConnection(id) {
    try {
      const { data } = await accountApi.test(id)
      message = data?.statusMessage ?? '연결 테스트 완료'
    } catch (e) {
      error = projectAccountError(e, '연결 테스트에 실패했습니다.')
    }
  }

  async function remove(id) {
    if (!confirm('이 계좌를 삭제할까요?')) return
    try {
      await accountApi.remove(id)
      message = '계좌를 삭제했습니다.'
      await load()
    } catch (e) {
      error = projectAccountError(e, '계좌 삭제에 실패했습니다.')
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
        <select value={form.brokerType} on:change={(event) => (form = selectBroker(form, brokerOptions, event.currentTarget.value))} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
          {#each brokerOptions as broker}
            <option value={broker.type} disabled={!broker.isImplemented}>{broker.displayName} · {broker.market}{broker.isImplemented ? '' : ' (준비 중)'}</option>
          {/each}
        </select>
        <select bind:value={form.environment} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
          {#each selectedBroker?.environments ?? [] as environment}
            <option value={environment}>{environment}</option>
          {/each}
        </select>
        {#if selectedBroker?.requiresAccountCredentials}
          <input bind:value={form.apiKey} placeholder="API Key" autocomplete="off" class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
          <input bind:value={form.apiSecret} type="password" placeholder="API Secret" autocomplete="new-password" class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        {/if}
        <textarea bind:value={form.notes} placeholder="메모" class="col-span-2 rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white"></textarea>
      </div>
      {#if selectedBroker}
        <div class="mt-4 rounded border border-gray-700 bg-gray-900/60 p-3 text-sm">
          <div class="mb-2 font-medium text-gray-200">실거래 지원 기능</div>
          {#if brokerCapabilityLabels(selectedBroker).length > 0}
            <div class="flex flex-wrap gap-2">
              {#each brokerCapabilityLabels(selectedBroker) as label}
                <span class="rounded-full bg-emerald-900/50 px-2 py-1 text-xs text-emerald-300">{label}</span>
              {/each}
            </div>
          {:else}
            <div class="text-amber-300">아직 연결 및 주문 기능을 사용할 수 없습니다.</div>
          {/if}
        </div>
      {/if}
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
              <td class="px-4 py-3">{row.accountName}</td>
              <td class="px-4 py-3">
                <div>{brokerFor(row.brokerType)?.displayName ?? row.brokerType}</div>
                <div class="mt-1 text-xs text-gray-400">{brokerCapabilityLabels(brokerFor(row.brokerType)).join(' · ') || '준비 중'}</div>
              </td>
              <td class="px-4 py-3">{row.environment}</td>
              <td class="px-4 py-3 font-mono text-xs">{row.apiKey}</td>
              <td class="px-4 py-3">{row.isActive ? 'Active' : row.isEnabled ? 'Enabled' : 'Disabled'}</td>
              <td class="px-4 py-3 text-right">
                <div class="flex justify-end gap-2">
                  <button on:click={() => testConnection(row.id)} disabled={!row.isEnabled || !brokerFor(row.brokerType)?.capabilities?.canReadAccount} class="rounded bg-gray-700 px-3 py-1 text-xs hover:bg-gray-600 disabled:cursor-not-allowed disabled:opacity-40">연결 확인</button>
                  <button on:click={() => activate(row.id)} disabled={!row.isEnabled || row.isActive || !brokerFor(row.brokerType)?.isImplemented} class="rounded bg-blue-700 px-3 py-1 text-xs hover:bg-blue-600 disabled:cursor-not-allowed disabled:opacity-40">사용 계좌로 지정</button>
                  <button on:click={() => remove(row.id)} class="rounded bg-red-700 px-3 py-1 text-xs hover:bg-red-600">Delete</button>
                </div>
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</div>

<script>
  import { onMount } from 'svelte'
  import { orderApi, signalApi } from '../api/endpoints'

  let loading = true
  let error = ''
  let rows = []
  let search = ''
  let sort = 'latest'
  let message = ''

  onMount(load)

  function pct(value, digits = 1) {
    return (Number(value ?? 0) * 100).toFixed(digits)
  }

  async function load() {
    loading = true
    try {
      const { data } = await signalApi.list({ search, sort })
      rows = data?.signals ?? data?.Signals ?? []
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '시그널을 불러오지 못했습니다.'
    } finally {
      loading = false
    }
  }

  async function execute(row) {
    try {
      await orderApi.executeSignal(row.Id ?? row.id)
      message = `${row.Symbol ?? row.symbol} 시그널을 실행했습니다.`
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '시그널 실행에 실패했습니다.'
    }
  }
</script>

<div class="flex-1 overflow-auto p-8">
  <div class="flex items-center justify-between mb-8">
    <h2 class="text-4xl font-bold">활성 시그널</h2>
    <div class="flex gap-3">
      <input bind:value={search} placeholder="종목 검색" class="rounded border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-white" />
      <select bind:value={sort} class="rounded border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-white">
        <option value="latest">최신순</option>
        <option value="confidence">신뢰도순</option>
        <option value="rr">R/R순</option>
      </select>
      <button on:click={load} class="bg-blue-600 hover:bg-blue-700 px-4 py-2 rounded transition text-sm">적용</button>
    </div>
  </div>

  {#if error}
    <div class="mb-6 rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
  {/if}
  {#if message}
    <div class="mb-6 rounded-lg border border-green-700 bg-green-900/20 p-4 text-green-300">{message}</div>
  {/if}

  {#if loading}
    <div class="text-gray-400">불러오는 중...</div>
  {:else if rows.length === 0}
    <div class="rounded-lg border border-gray-700 bg-gray-800 p-8 text-gray-400">활성 시그널이 없습니다.</div>
  {:else}
    <div class="grid grid-cols-2 gap-4">
      {#each rows as row}
        <div class="rounded-lg border border-gray-700 bg-gray-800 p-5">
          <div class="flex items-start justify-between mb-3">
            <div>
              <div class="font-mono text-xl text-blue-400">{row.Symbol ?? row.symbol}</div>
              <div class="text-sm text-gray-400">{row.Pattern ?? row.pattern}</div>
            </div>
            <div class="rounded bg-blue-950/60 px-2 py-1 text-xs text-blue-300">
              {pct(row.Confidence ?? row.confidence ?? 0)}%
            </div>
          </div>
          <div class="grid grid-cols-3 gap-3 text-sm">
            <div><div class="text-gray-500">Entry</div><div>{(row.EntryPrice ?? row.entryPrice ?? 0).toFixed(2)}</div></div>
            <div><div class="text-gray-500">Stop</div><div class="text-red-300">{(row.StopLossPrice ?? row.stopLossPrice ?? 0).toFixed(2)}</div></div>
            <div><div class="text-gray-500">Target</div><div class="text-green-300">{(row.TargetPrice ?? row.targetPrice ?? 0).toFixed(2)}</div></div>
          </div>
          <div class="mt-4 flex justify-between text-xs text-gray-400">
            <span>R/R {(row.RiskReward ?? row.riskReward ?? 0).toFixed(2)}</span>
            <div class="flex items-center gap-3">
              <span>WinRate {pct(row.PatternWinRate ?? row.patternWinRate ?? 0)}%</span>
              <button on:click={() => execute(row)} class="rounded bg-blue-700 px-3 py-1 text-xs text-white hover:bg-blue-600">
                실행
              </button>
            </div>
          </div>
        </div>
      {/each}
    </div>
  {/if}
</div>

<script>
  import { onMount } from 'svelte'
  import { tradeApi } from '../api/endpoints'
  import { tradeApiError } from '../features/trades/tradeActivityModel.js'

  let loading = true
  let error = ''
  let rows = []
  let pattern = ''

  onMount(load)

  function pct(value, digits = 2) {
    return (Number(value ?? 0) * 100).toFixed(digits)
  }

  async function load() {
    loading = true
    try {
      const { data } = await tradeApi.history({ take: 100, pattern: pattern || undefined })
      rows = data.trades
      error = ''
    } catch (e) {
      error = tradeApiError(e, '거래 내역을 불러오지 못했습니다.')
    } finally {
      loading = false
    }
  }
</script>

<div class="flex-1 overflow-auto p-8">
  <div class="flex items-center justify-between mb-8">
    <h2 class="text-4xl font-bold">거래 내역</h2>
    <div class="flex gap-3">
      <input bind:value={pattern} placeholder="패턴 필터" class="rounded border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-white" />
      <button on:click={load} class="bg-blue-600 hover:bg-blue-700 px-4 py-2 rounded transition text-sm">조회</button>
    </div>
  </div>

  {#if error}
    <div class="mb-6 rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
  {/if}

  {#if loading}
    <div class="text-gray-400">불러오는 중...</div>
  {:else if rows.length === 0}
    <div class="rounded-lg border border-gray-700 bg-gray-800 p-8 text-gray-400">거래 내역이 없습니다.</div>
  {:else}
    <div class="overflow-hidden rounded-lg border border-gray-700 bg-gray-800">
      <table class="w-full text-sm">
        <thead class="bg-gray-900/80 text-gray-400">
          <tr>
            <th class="px-4 py-3 text-left">Symbol</th>
            <th class="px-4 py-3 text-left">Pattern</th>
            <th class="px-4 py-3 text-right">Entry</th>
            <th class="px-4 py-3 text-right">Exit</th>
            <th class="px-4 py-3 text-right">PnL</th>
            <th class="px-4 py-3 text-right">PnL %</th>
            <th class="px-4 py-3 text-left">Exit Reason</th>
          </tr>
        </thead>
        <tbody>
          {#each rows as row}
            <tr class="border-t border-gray-700">
              <td class="px-4 py-3 font-mono text-blue-400">{row.symbol}</td>
              <td class="px-4 py-3">{row.patternName}</td>
              <td class="px-4 py-3 text-right">{row.entryPrice.toFixed(2)}</td>
              <td class="px-4 py-3 text-right">{row.exitPrice.toFixed(2)}</td>
              <td class="px-4 py-3 text-right {row.pnL >= 0 ? 'text-green-300' : 'text-red-300'}">{row.pnL.toFixed(2)}</td>
              <td class="px-4 py-3 text-right {row.pnLPercent >= 0 ? 'text-green-300' : 'text-red-300'}">{pct(row.pnLPercent)}%</td>
              <td class="px-4 py-3">{row.exitReason}</td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</div>

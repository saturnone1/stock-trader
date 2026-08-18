<script>
  import { onMount } from 'svelte'
  import { patternStatsApi } from '../api/endpoints'

  let loading = true
  let error = ''
  let rows = []

  onMount(load)

  function pct(value, digits = 2) {
    return `${(Number(value ?? 0) * 100).toFixed(digits)}%`
  }

  async function load() {
    loading = true
    try {
      const { data } = await patternStatsApi.list()
      rows = data?.stats ?? []
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '패턴 통계를 불러오지 못했습니다.'
    } finally {
      loading = false
    }
  }
</script>

<div class="flex-1 overflow-auto p-8">
  <div class="mb-8 flex items-center justify-between">
    <div>
      <h2 class="text-4xl font-bold">패턴 통계</h2>
      <div class="mt-2 text-sm text-gray-400">기대값, 승률, 손익비 기준 패턴 성과 비교</div>
    </div>
    <button on:click={load} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">새로고침</button>
  </div>

  {#if error}
    <div class="mb-6 rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
  {/if}

  {#if loading}
    <div class="text-gray-400">불러오는 중...</div>
  {:else if rows.length === 0}
    <div class="rounded-lg border border-gray-700 bg-gray-800 p-8 text-gray-400">패턴 통계가 없습니다.</div>
  {:else}
    <div class="mb-6 grid grid-cols-4 gap-4">
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-4"><div class="text-sm text-gray-400">패턴 수</div><div class="mt-2 text-2xl font-bold">{rows.length}</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-4"><div class="text-sm text-gray-400">양수 기대값</div><div class="mt-2 text-2xl font-bold text-green-300">{rows.filter((r) => (r.expectancy ?? 0) > 0).length}</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-4"><div class="text-sm text-gray-400">최고 기대값</div><div class="mt-2 text-2xl font-bold">{Number(rows[0]?.expectancy ?? 0).toFixed(3)}</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-4"><div class="text-sm text-gray-400">최고 승률</div><div class="mt-2 text-2xl font-bold">{pct(Math.max(...rows.map((r) => Number(r.winRate ?? 0))), 1)}</div></div>
    </div>

    <div class="overflow-hidden rounded-lg border border-gray-700 bg-gray-800">
      <table class="w-full text-sm">
        <thead class="bg-gray-900/80 text-gray-400">
          <tr>
            <th class="px-4 py-3 text-left">Pattern</th>
            <th class="px-4 py-3 text-left">Symbol</th>
            <th class="px-4 py-3 text-right">Sample</th>
            <th class="px-4 py-3 text-right">Win Rate</th>
            <th class="px-4 py-3 text-right">Avg Win</th>
            <th class="px-4 py-3 text-right">Avg Loss</th>
            <th class="px-4 py-3 text-right">Max DD</th>
            <th class="px-4 py-3 text-right">Expectancy</th>
            <th class="px-4 py-3 text-right">Profit Factor</th>
          </tr>
        </thead>
        <tbody>
          {#each rows as row}
            <tr class="border-t border-gray-700 hover:bg-gray-700/30">
              <td class="px-4 py-3 font-medium">{row.pattern}</td>
              <td class="px-4 py-3 font-mono text-blue-400">{row.symbol}</td>
              <td class="px-4 py-3 text-right">{row.sampleSize}</td>
              <td class="px-4 py-3 text-right">{pct(row.winRate, 1)}</td>
              <td class="px-4 py-3 text-right text-green-300">{pct(row.avgWinPercent)}</td>
              <td class="px-4 py-3 text-right text-red-300">{pct(row.avgLossPercent)}</td>
              <td class="px-4 py-3 text-right text-red-300">{pct(row.maxDrawdownPercent)}</td>
              <td class="px-4 py-3 text-right {(row.expectancy ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}">{Number(row.expectancy ?? 0).toFixed(3)}</td>
              <td class="px-4 py-3 text-right">{Number(row.profitFactor ?? 0).toFixed(2)}</td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</div>

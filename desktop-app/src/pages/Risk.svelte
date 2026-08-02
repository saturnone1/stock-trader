<script>
  import { onMount } from 'svelte'
  import { riskApi } from '../api/endpoints'

  let loading = true
  let error = ''
  let data = null

  onMount(load)

  function pct(value, digits = 2) {
    return (Number(value ?? 0) * 100).toFixed(digits)
  }

  async function load() {
    loading = true
    try {
      const res = await riskApi.get()
      data = res.data
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '리스크 상태를 불러오지 못했습니다.'
    } finally {
      loading = false
    }
  }
</script>

<div class="flex-1 overflow-auto p-8">
  <div class="flex items-center justify-between mb-8">
    <h2 class="text-4xl font-bold">리스크 모니터</h2>
    <button on:click={load} class="bg-blue-600 hover:bg-blue-700 px-4 py-2 rounded transition text-sm">새로고침</button>
  </div>

  {#if error}
    <div class="mb-6 rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
  {/if}

  {#if loading}
    <div class="text-gray-400">불러오는 중...</div>
  {:else if data}
    <div class="grid grid-cols-4 gap-4 mb-8">
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Daily PnL</div><div class="text-2xl font-bold">{(data.RiskState?.DailyPnL ?? 0).toFixed(2)}</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Daily PnL %</div><div class="text-2xl font-bold">{pct(data.RiskState?.DailyPnLPercent ?? 0)}%</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Open Positions</div><div class="text-2xl font-bold">{data.RiskState?.OpenPositionCount ?? 0}</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Trading Halted</div><div class={`text-2xl font-bold ${data.RiskState?.IsTradingHalted ? 'text-red-400' : 'text-green-400'}`}>{data.RiskState?.IsTradingHalted ? 'YES' : 'NO'}</div></div>
    </div>

    <div class="grid grid-cols-2 gap-6 mb-8">
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-6">
        <h3 class="font-bold mb-4">리스크 설정</h3>
        <div class="space-y-2 text-sm">
          <div class="flex justify-between"><span class="text-gray-400">계좌 규모</span><span>{data.Settings?.AccountSize ?? 0}</span></div>
          <div class="flex justify-between"><span class="text-gray-400">거래당 리스크</span><span>{pct(data.Settings?.RiskPerTradePercent ?? 0)}%</span></div>
          <div class="flex justify-between"><span class="text-gray-400">일일 손실 제한</span><span>{pct(data.Settings?.DailyLossLimitPercent ?? 0)}%</span></div>
          <div class="flex justify-between"><span class="text-gray-400">최대 포지션 수</span><span>{data.Settings?.MaxTotalPositions ?? 0}</span></div>
          <div class="flex justify-between"><span class="text-gray-400">섹터당 최대 포지션</span><span>{data.Settings?.MaxPositionsPerSector ?? 0}</span></div>
          <div class="flex justify-between"><span class="text-gray-400">최소 신뢰도</span><span>{pct(data.Settings?.MinConfidence ?? 0, 1)}%</span></div>
        </div>
      </div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-6">
        <h3 class="font-bold mb-4">섹터별 포지션</h3>
        <div class="space-y-2 text-sm">
          {#each Object.entries(data.RiskState?.PositionsPerSector ?? {}) as [sector, count]}
            <div class="flex justify-between"><span class="text-gray-400">{sector}</span><span>{count}</span></div>
          {/each}
        </div>
        <div class="mt-4 border-t border-gray-700 pt-4">
          <div class="flex justify-between"><span class="text-gray-400">총 미실현 손익</span><span class={(data.TotalUnrealizedPnL ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}>{(data.TotalUnrealizedPnL ?? 0).toFixed(2)}</span></div>
        </div>
      </div>
    </div>

    <div class="overflow-hidden rounded-lg border border-gray-700 bg-gray-800">
      <div class="border-b border-gray-700 px-5 py-4 font-bold">포지션 R-Multiple</div>
      <table class="w-full text-sm">
        <thead class="bg-gray-900/80 text-gray-400">
          <tr>
            <th class="px-4 py-3 text-left">Symbol</th>
            <th class="px-4 py-3 text-left">Pattern</th>
            <th class="px-4 py-3 text-right">Entry</th>
            <th class="px-4 py-3 text-right">Current</th>
            <th class="px-4 py-3 text-right">R</th>
            <th class="px-4 py-3 text-right">PnL</th>
          </tr>
        </thead>
        <tbody>
          {#each data.PositionRMultiples ?? [] as row}
            <tr class="border-t border-gray-700">
              <td class="px-4 py-3 font-mono text-blue-400">{row.Symbol}</td>
              <td class="px-4 py-3">{row.Pattern}</td>
              <td class="px-4 py-3 text-right">{(row.EntryPrice ?? 0).toFixed(2)}</td>
              <td class="px-4 py-3 text-right">{(row.CurrentPrice ?? 0).toFixed(2)}</td>
              <td class="px-4 py-3 text-right">{(row.RMultiple ?? 0).toFixed(2)}</td>
              <td class="px-4 py-3 text-right {(row.UnrealizedPnL ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}">{(row.UnrealizedPnL ?? 0).toFixed(2)}</td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</div>

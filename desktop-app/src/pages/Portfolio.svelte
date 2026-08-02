<script>
  import { onMount } from 'svelte'
  import { orderApi, portfolioApi } from '../api/endpoints'

  let loading = true
  let error = ''
  let message = ''
  let holdings = null
  let performance = null

  onMount(load)

  function pct(value, digits = 1) {
    return (Number(value ?? 0) * 100).toFixed(digits)
  }

  async function load() {
    loading = true
    try {
      const [p1, p2] = await Promise.all([portfolioApi.get(), portfolioApi.performance()])
      holdings = p1.data
      performance = p2.data
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '포트폴리오 데이터를 불러오지 못했습니다.'
    } finally {
      loading = false
    }
  }

  async function closePosition(symbol) {
    try {
      await orderApi.closePosition(symbol)
      message = `${symbol} 포지션 청산을 요청했습니다.`
      error = ''
      await load()
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '포지션 청산에 실패했습니다.'
    }
  }
</script>

<div class="flex-1 overflow-auto p-8">
  <div class="flex items-center justify-between mb-8">
    <h2 class="text-4xl font-bold">포트폴리오 분석</h2>
    <button on:click={load} class="bg-blue-600 hover:bg-blue-700 px-4 py-2 rounded transition text-sm">새로고침</button>
  </div>

  {#if error}
    <div class="mb-6 rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
  {/if}
  {#if message}
    <div class="mb-6 rounded-lg border border-green-700 bg-green-900/20 p-4 text-green-300">{message}</div>
  {/if}

  {#if loading}
    <div class="text-gray-400">불러오는 중...</div>
  {:else}
    <div class="grid grid-cols-4 gap-4 mb-8">
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Open Positions</div><div class="text-2xl font-bold">{holdings?.PositionCount ?? 0}</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Unrealized PnL</div><div class={`text-2xl font-bold ${(holdings?.TotalUnrealizedPnL ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{(holdings?.TotalUnrealizedPnL ?? 0).toFixed(2)}</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Win Rate</div><div class="text-2xl font-bold">{pct(performance?.WinRate ?? 0)}%</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Max Drawdown</div><div class="text-2xl font-bold text-red-300">{pct(performance?.MaxDrawdown ?? 0, 2)}%</div></div>
    </div>

    <div class="overflow-hidden rounded-lg border border-gray-700 bg-gray-800 mb-8">
      <div class="border-b border-gray-700 px-5 py-4 font-bold">보유 포지션</div>
      <table class="w-full text-sm">
        <thead class="bg-gray-900/80 text-gray-400">
          <tr>
            <th class="px-4 py-3 text-left">Symbol</th>
            <th class="px-4 py-3 text-left">Sector</th>
            <th class="px-4 py-3 text-right">Qty</th>
            <th class="px-4 py-3 text-right">Entry</th>
            <th class="px-4 py-3 text-right">Current</th>
            <th class="px-4 py-3 text-right">PnL</th>
            <th class="px-4 py-3 text-right">Action</th>
          </tr>
        </thead>
        <tbody>
          {#each holdings?.Positions ?? [] as row}
            <tr class="border-t border-gray-700">
              <td class="px-4 py-3 font-mono text-blue-400">{row.Symbol}</td>
              <td class="px-4 py-3">{row.Sector}</td>
              <td class="px-4 py-3 text-right">{row.Quantity}</td>
              <td class="px-4 py-3 text-right">{(row.EntryPrice ?? 0).toFixed(2)}</td>
              <td class="px-4 py-3 text-right">{(row.CurrentPrice ?? 0).toFixed(2)}</td>
              <td class="px-4 py-3 text-right {(row.UnrealizedPnL ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}">{(row.UnrealizedPnL ?? 0).toFixed(2)}</td>
              <td class="px-4 py-3 text-right">
                <button on:click={() => closePosition(row.Symbol)} class="rounded bg-red-700 px-3 py-1 text-xs text-white hover:bg-red-600">
                  청산
                </button>
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>

    <div class="overflow-hidden rounded-lg border border-gray-700 bg-gray-800">
      <div class="border-b border-gray-700 px-5 py-4 font-bold">패턴 성과</div>
      <table class="w-full text-sm">
        <thead class="bg-gray-900/80 text-gray-400">
          <tr>
            <th class="px-4 py-3 text-left">Pattern</th>
            <th class="px-4 py-3 text-right">Sample</th>
            <th class="px-4 py-3 text-right">Win Rate</th>
            <th class="px-4 py-3 text-right">Expectancy</th>
            <th class="px-4 py-3 text-right">Profit Factor</th>
          </tr>
        </thead>
        <tbody>
          {#each (performance?.PatternStats ?? []).slice(0, 20) as row}
            <tr class="border-t border-gray-700">
              <td class="px-4 py-3">{row.Pattern}</td>
              <td class="px-4 py-3 text-right">{row.SampleSize}</td>
              <td class="px-4 py-3 text-right">{pct(row.WinRate ?? 0)}%</td>
              <td class="px-4 py-3 text-right">{(row.Expectancy ?? 0).toFixed(3)}</td>
              <td class="px-4 py-3 text-right">{(row.ProfitFactor ?? 0).toFixed(2)}</td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</div>

<script>
  import { onMount } from 'svelte'
  import { orderApi, portfolioApi } from '../api/endpoints'

  let loading = true
  let error = ''
  let message = ''
  let holdings = null
  let performance = null
  let busySymbol = ''

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
    busySymbol = symbol
    try {
      const response = await orderApi.closePosition(symbol)
      message = response.data?.message || `${symbol} 포지션 청산을 요청했습니다.`
      error = ''
      await load()
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '포지션 청산에 실패했습니다.'
    } finally {
      busySymbol = ''
    }
  }

  async function reconcilePositionExit(symbol) {
    busySymbol = symbol
    try {
      const response = await orderApi.reconcilePositionExit(symbol)
      message = response.data?.message || `${symbol} 청산 주문 상태를 다시 확인했습니다.`
      error = ''
      await load()
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '청산 주문 상태 확인에 실패했습니다.'
    } finally {
      busySymbol = ''
    }
  }

  function exitStatusLabel(row) {
    if (row.exitStatus === 'SubmissionUnconfirmed') return '주문 ID 확인 필요'
    if (row.exitStatus === 'AwaitingBroker') return '브로커 처리 중'
    return '보유 중'
  }

  function pendingTime(row) {
    const seconds = Number(row.exitPendingSeconds ?? 0)
    if (seconds < 60) return `${seconds}초`
    if (seconds < 3600) return `${Math.floor(seconds / 60)}분`
    return `${Math.floor(seconds / 3600)}시간`
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
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Open Positions</div><div class="text-2xl font-bold">{holdings?.positionCount ?? 0}</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Unrealized PnL</div><div class={`text-2xl font-bold ${(holdings?.totalUnrealizedPnL ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{(holdings?.totalUnrealizedPnL ?? 0).toFixed(2)}</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Win Rate</div><div class="text-2xl font-bold">{pct(performance?.winRate ?? 0)}%</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Max Drawdown</div><div class="text-2xl font-bold text-red-300">{pct(performance?.maxDrawdown ?? 0, 2)}%</div></div>
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
            <th class="px-4 py-3 text-left">청산 상태</th>
            <th class="px-4 py-3 text-right">Action</th>
          </tr>
        </thead>
        <tbody>
          {#each holdings?.positions ?? [] as row}
            <tr class="border-t border-gray-700">
              <td class="px-4 py-3 font-mono text-blue-400">{row.symbol}</td>
              <td class="px-4 py-3">{row.sector}</td>
              <td class="px-4 py-3 text-right">{row.quantity}</td>
              <td class="px-4 py-3 text-right">{(row.entryPrice ?? 0).toFixed(2)}</td>
              <td class="px-4 py-3 text-right">{(row.currentPrice ?? 0).toFixed(2)}</td>
              <td class="px-4 py-3 text-right {(row.unrealizedPnL ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}">{(row.unrealizedPnL ?? 0).toFixed(2)}</td>
              <td class="px-4 py-3">
                <div class={row.exitStatus === 'Ready' ? 'text-gray-300' : row.exitStatus === 'AwaitingBroker' ? 'text-blue-300' : 'text-amber-300'}>
                  {exitStatusLabel(row)}
                </div>
                {#if row.exitStatus !== 'Ready'}
                  <div class="mt-1 text-xs text-gray-500">{row.exitRequestReason || '청산 요청'} · {pendingTime(row)}</div>
                {/if}
              </td>
              <td class="px-4 py-3 text-right">
                {#if row.exitStatus === 'Ready'}
                  <button disabled={busySymbol === row.symbol} on:click={() => closePosition(row.symbol)} class="rounded bg-red-700 px-3 py-1 text-xs text-white hover:bg-red-600 disabled:cursor-wait disabled:opacity-50">
                    {busySymbol === row.symbol ? '요청 중…' : '청산'}
                  </button>
                {:else}
                  <button disabled={busySymbol === row.symbol} on:click={() => reconcilePositionExit(row.symbol)} class="rounded bg-blue-700 px-3 py-1 text-xs text-white hover:bg-blue-600 disabled:cursor-wait disabled:opacity-50">
                    {busySymbol === row.symbol ? '확인 중…' : '상태 재확인'}
                  </button>
                {/if}
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
          {#each (performance?.patternStats ?? []).slice(0, 20) as row}
            <tr class="border-t border-gray-700">
              <td class="px-4 py-3">{row.pattern}</td>
              <td class="px-4 py-3 text-right">{row.sampleSize}</td>
              <td class="px-4 py-3 text-right">{pct(row.winRate ?? 0)}%</td>
              <td class="px-4 py-3 text-right">{(row.expectancy ?? 0).toFixed(3)}</td>
              <td class="px-4 py-3 text-right">{(row.profitFactor ?? 0).toFixed(2)}</td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</div>

<script>
  import { formatMoney, formatPercent } from './backtestResearch'

  export let result
</script>

<div class="grid grid-cols-1 gap-4 xl:grid-cols-3">
  <div class="rounded-2xl border border-gray-800 bg-gray-950 p-5">
    <div class="mb-3 text-sm font-semibold">성과 지표</div>
    <div class="grid grid-cols-2 gap-3 text-sm">
      <div class="rounded border border-gray-800 bg-gray-900 p-3">슬리피지 ${formatMoney(result.totalSlippageCost)}</div>
      <div class="rounded border border-gray-800 bg-gray-900 p-3">수수료 ${formatMoney(result.totalCommissionCost)}</div>
      <div class="rounded border border-gray-800 bg-gray-900 p-3">가중전략 적용 {result.weightStrategyApplied ? '예' : '아니오'}</div>
      <div class="rounded border border-gray-800 bg-gray-900 p-3">축소 거래 {result.weightReducedTrades ?? 0}</div>
    </div>
  </div>

  <div class="rounded-2xl border border-gray-800 bg-gray-950 p-5 xl:col-span-2">
    <div class="mb-3 text-sm font-semibold">종목별 성과</div>
    {#if result.perSymbol?.length}
      <div class="overflow-auto">
        <table class="min-w-full text-sm">
          <thead class="text-left text-gray-500">
            <tr>
              <th class="px-3 py-2">종목</th>
              <th class="px-3 py-2">거래 수</th>
              <th class="px-3 py-2">승률</th>
              <th class="px-3 py-2">총 손익</th>
              <th class="px-3 py-2">평균 손익률</th>
            </tr>
          </thead>
          <tbody>
            {#each result.perSymbol as item}
              <tr class="border-t border-gray-800">
                <td class="px-3 py-2 font-medium text-white">{item.symbol}</td>
                <td class="px-3 py-2">{item.tradeCount}</td>
                <td class="px-3 py-2">{formatPercent(item.winRate, 1)}</td>
                <td class={`px-3 py-2 ${Number(item.totalPnL ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>${formatMoney(item.totalPnL)}</td>
                <td class={`px-3 py-2 ${Number(item.avgPnLPercent ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{formatPercent(item.avgPnLPercent)}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {:else}
      <div class="text-sm text-gray-400">종목별 결과가 없습니다.</div>
    {/if}
  </div>
</div>

<div class="grid grid-cols-1 gap-4 xl:grid-cols-2">
  <div class="rounded-2xl border border-gray-800 bg-gray-950 p-5">
    <div class="mb-3 text-sm font-semibold">전략별 성과</div>
    {#if (result.perStrategy && Object.keys(result.perStrategy).length > 0) || (result.perPattern && Object.keys(result.perPattern).length > 0)}
      <div class="space-y-3">
        {#each Object.entries(result.perStrategy && Object.keys(result.perStrategy).length > 0 ? result.perStrategy : result.perPattern) as [patternName, stats]}
          <div class="rounded-lg border border-gray-800 bg-gray-900 p-4 text-sm">
            <div class="mb-2 font-medium text-white">{patternName}</div>
            <div class="grid grid-cols-2 gap-2 text-gray-300">
              <div>표본 수 {stats.sampleSize}</div>
              <div>승률 {formatPercent(stats.winRate, 1)}</div>
              <div>기대값 {formatPercent(stats.expectancy)}</div>
              <div>프로핏 팩터 {Number(stats.profitFactor ?? 0).toFixed(2)}</div>
              <div>평균 이익 {formatPercent(stats.avgWinPercent)}</div>
              <div>평균 손실 {formatPercent(stats.avgLossPercent)}</div>
            </div>
          </div>
        {/each}
      </div>
    {:else}
      <div class="text-sm text-gray-400">전략별 결과가 없습니다.</div>
    {/if}
  </div>

  <div class="rounded-2xl border border-gray-800 bg-gray-950 p-5">
    <div class="mb-3 text-sm font-semibold">레짐별 성과</div>
    {#if result.perRegimeStats && Object.keys(result.perRegimeStats).length > 0}
      <div class="space-y-3">
        {#each Object.entries(result.perRegimeStats) as [regime, stats]}
          <div class="rounded-lg border border-gray-800 bg-gray-900 p-4 text-sm">
            <div class="mb-2 font-medium text-white">{regime}</div>
            <div class="grid grid-cols-2 gap-2 text-gray-300">
              <div>거래 수 {stats.tradeCount}</div>
              <div>승률 {formatPercent(stats.winRate, 1)}</div>
              <div>총 수익률 {formatPercent(stats.totalReturn)}</div>
              <div>샤프 {Number(stats.sharpeRatio ?? 0).toFixed(2)}</div>
              <div>평균 수익률 {formatPercent(stats.avgReturnPercent)}</div>
              <div>최대 낙폭 {formatPercent(stats.maxDrawdown)}</div>
            </div>
          </div>
        {/each}
      </div>
    {:else}
      <div class="text-sm text-gray-400">레짐별 결과가 없습니다.</div>
    {/if}
  </div>
</div>

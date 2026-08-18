<script>
  import { formatMoney, formatPercent } from './backtestResearch'

  export let result
</script>

{#if result.walkForward}
  <div class="rounded-2xl border border-gray-800 bg-gray-950 p-5">
    <div class="mb-4 text-sm font-semibold">워크포워드 결과</div>
    <div class="mb-4 grid grid-cols-2 gap-4 xl:grid-cols-5">
      <div class="rounded border border-gray-800 bg-gray-900 p-3 text-sm">OOS 수익률 {formatPercent(result.walkForward.aggregateOosReturnPercent)}</div>
      <div class="rounded border border-gray-800 bg-gray-900 p-3 text-sm">OOS 낙폭 {formatPercent(result.walkForward.aggregateOosMaxDrawdown)}</div>
      <div class="rounded border border-gray-800 bg-gray-900 p-3 text-sm">OOS 승률 {formatPercent(result.walkForward.aggregateOosWinRate, 1)}</div>
      <div class="rounded border border-gray-800 bg-gray-900 p-3 text-sm">OOS 평균 샤프 {Number(result.walkForward.aggregateOosSharpe ?? 0).toFixed(2)}</div>
      <div class="rounded border border-gray-800 bg-gray-900 p-3 text-sm">효율 {Number(result.walkForward.walkForwardEfficiency ?? 0).toFixed(2)}</div>
    </div>
    <div class="overflow-auto">
      <table class="min-w-full text-sm">
        <thead class="text-left text-gray-500">
          <tr>
            <th class="px-3 py-2">IS 구간</th>
            <th class="px-3 py-2">OOS 구간</th>
            <th class="px-3 py-2">IS 거래</th>
            <th class="px-3 py-2">IS 수익률</th>
            <th class="px-3 py-2">OOS 거래</th>
            <th class="px-3 py-2">OOS 수익률</th>
            <th class="px-3 py-2">OOS 낙폭</th>
            <th class="px-3 py-2">효율</th>
          </tr>
        </thead>
        <tbody>
          {#each result.walkForward.windows ?? [] as window}
            <tr class="border-t border-gray-800">
              <td class="px-3 py-2">{window.isFrom} ~ {window.isTo}</td>
              <td class="px-3 py-2">{window.oosFrom} ~ {window.oosTo}</td>
              <td class="px-3 py-2">{window.inSampleTrades}</td>
              <td class="px-3 py-2">{formatPercent(window.inSampleReturnPercent)}</td>
              <td class="px-3 py-2">{window.outOfSampleTrades}</td>
              <td class="px-3 py-2">{formatPercent(window.outOfSampleReturnPercent)}</td>
              <td class="px-3 py-2">{formatPercent(window.outOfSampleMaxDrawdown)}</td>
              <td class="px-3 py-2">{Number(window.efficiency ?? 0).toFixed(2)}</td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  </div>
{/if}

{#if result.monteCarlo}
  <div class="rounded-2xl border border-gray-800 bg-gray-950 p-5">
    <div class="mb-4 text-sm font-semibold">몬테카를로 결과</div>
    <div class="grid grid-cols-2 gap-4 xl:grid-cols-5">
      <div class="rounded border border-gray-800 bg-gray-900 p-3 text-sm">시뮬레이션 {result.monteCarlo.simulations}</div>
      <div class="rounded border border-gray-800 bg-gray-900 p-3 text-sm">중간 최종자산 ${formatMoney(result.monteCarlo.medianFinalEquity)}</div>
      <div class="rounded border border-gray-800 bg-gray-900 p-3 text-sm">평균 최종자산 ${formatMoney(result.monteCarlo.meanFinalEquity)}</div>
      <div class="rounded border border-gray-800 bg-gray-900 p-3 text-sm">5% 자산 ${formatMoney(result.monteCarlo.percentile5Equity)}</div>
      <div class="rounded border border-gray-800 bg-gray-900 p-3 text-sm">손실 확률 {formatPercent(result.monteCarlo.probabilityOfLoss, 1)}</div>
    </div>
  </div>
{/if}

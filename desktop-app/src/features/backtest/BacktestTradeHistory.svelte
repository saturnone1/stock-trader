<script>
  import { formatDate, formatPercent } from './backtestResearch'

  export let trades = []
</script>

<div class="rounded-2xl border border-gray-800 bg-gray-950 p-5">
  <div class="mb-4 text-sm font-semibold">최근 거래</div>
  {#if trades?.length}
    <div class="overflow-auto">
      <table class="min-w-full text-sm">
        <thead class="text-left text-gray-500">
          <tr>
            <th class="px-3 py-2">종목</th>
            <th class="px-3 py-2">패턴</th>
            <th class="px-3 py-2">진입일</th>
            <th class="px-3 py-2">청산일</th>
            <th class="px-3 py-2">수익률</th>
            <th class="px-3 py-2">청산 사유</th>
          </tr>
        </thead>
        <tbody>
          {#each trades.slice(0, 30) as trade}
            <tr class="border-t border-gray-800">
              <td class="px-3 py-2 font-medium text-white">{trade.symbol}</td>
              <td class="px-3 py-2">{trade.pattern}</td>
              <td class="px-3 py-2">{formatDate(trade.entryTime)}</td>
              <td class="px-3 py-2">{formatDate(trade.exitTime)}</td>
              <td class={`px-3 py-2 ${Number(trade.returnPct ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{formatPercent(trade.returnPct)}</td>
              <td class="px-3 py-2 text-gray-400">{trade.exitReason}</td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {:else}
    <div class="text-sm text-gray-400">거래 내역이 없습니다.</div>
  {/if}
</div>

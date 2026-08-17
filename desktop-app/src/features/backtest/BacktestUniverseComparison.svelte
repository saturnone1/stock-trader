<script>
  import { formatPercent } from './backtestResearch'

  export let rows = []
</script>

<section class="rounded-2xl border border-emerald-800/40 bg-emerald-950/10 p-5">
  <div class="text-xl font-semibold text-white">필터 전/후 기준 비교</div>
  <div class="mt-2 text-sm text-gray-400">각 유니버스의 기본 패턴 결과만 따로 뽑아 현재 입력 대비 얼마나 줄었는지 먼저 확인합니다.</div>
  <div class="mt-4 overflow-auto">
    <table class="min-w-full text-sm">
      <thead class="text-left text-gray-500"><tr><th class="px-3 py-2">유니버스</th><th class="px-3 py-2">종목 수</th><th class="px-3 py-2">종목 감소</th><th class="px-3 py-2">총 수익률</th><th class="px-3 py-2">최대 낙폭</th><th class="px-3 py-2">샤프</th><th class="px-3 py-2">거래 수</th></tr></thead>
      <tbody>
        {#each rows as row}
          <tr class="border-t border-gray-800">
            <td class="px-3 py-2 font-medium text-white">{row.label}</td><td class="px-3 py-2 text-gray-300">{row.symbolCount}</td>
            <td class={`px-3 py-2 ${row.symbolReduction >= 0 ? 'text-emerald-300' : 'text-red-300'}`}>{row.symbolReduction > 0 ? '+' : ''}{row.symbolReduction}</td>
            <td class={`px-3 py-2 ${Number(row.totalReturn ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{formatPercent(row.totalReturn)}</td>
            <td class="px-3 py-2 text-red-300">{formatPercent(row.maxDrawdown)}</td><td class="px-3 py-2 text-gray-300">{Number(row.sharpeRatio ?? 0).toFixed(2)}</td><td class="px-3 py-2 text-gray-300">{row.totalTrades ?? 0}</td>
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
</section>

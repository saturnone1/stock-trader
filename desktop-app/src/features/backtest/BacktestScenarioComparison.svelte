<script>
  import { formatPercent, formatSignedPercent } from './backtestResearch'

  export let rows = []
  export let activeScenarioKey = ''
  export let runStatus = ''
  export let onSelect
</script>

<section class="rounded-2xl border border-gray-800 bg-gray-950 p-5">
  <div class="flex items-center justify-between gap-4">
    <div>
      <div class="text-xl font-semibold text-white">타이밍·팩터 비교 결과</div>
      <div class="mt-2 text-sm text-gray-400">행을 클릭하면 아래 상세 결과가 해당 시나리오와 유니버스로 바뀝니다.</div>
    </div>
    {#if runStatus}<div class="rounded bg-blue-950/40 px-3 py-2 text-sm text-blue-200">{runStatus}</div>{/if}
  </div>

  <div class="mt-4 overflow-auto">
    <table class="min-w-full text-sm">
      <thead class="text-left text-gray-500">
        <tr>
          <th class="px-3 py-2">유니버스</th><th class="px-3 py-2">종목 수</th><th class="px-3 py-2">시나리오</th><th class="px-3 py-2">총 수익률</th>
          <th class="px-3 py-2">최대 낙폭</th><th class="px-3 py-2">샤프</th><th class="px-3 py-2">거래 수</th><th class="px-3 py-2">낙폭 개선</th>
          <th class="px-3 py-2">거래 감소</th><th class="px-3 py-2">휩소 감소</th><th class="px-3 py-2">곡선 안정</th>
        </tr>
      </thead>
      <tbody>
        {#each rows as row}
          <tr class={`cursor-pointer border-t border-gray-800 transition ${activeScenarioKey === row.key ? 'bg-blue-950/20' : 'hover:bg-gray-900'}`} on:click={() => onSelect(row.key)}>
            <td class="px-3 py-2">
              <div class="font-medium text-white">{row.comparisonGroupLabel}</div>
              {#if row.isBaseline}<div class="text-xs text-emerald-300">이 유니버스의 기준선</div>{/if}
            </td>
            <td class="px-3 py-2 text-gray-300">{row.symbolCount}</td>
            <td class="px-3 py-2"><div class="font-medium text-white">{row.label}</div>{#if row.isBaseline}<div class="text-xs text-blue-300">기본 패턴 시나리오</div>{/if}</td>
            <td class={`px-3 py-2 ${Number(row.data.totalReturn ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{formatPercent(row.data.totalReturn)}</td>
            <td class="px-3 py-2 text-red-300">{formatPercent(row.data.maxDrawdown)}</td><td class="px-3 py-2">{Number(row.data.sharpeRatio ?? 0).toFixed(2)}</td><td class="px-3 py-2">{row.data.totalTrades ?? 0}</td>
            <td class={`px-3 py-2 ${row.delta && Number(row.delta.drawdownImprovement) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{row.delta ? formatSignedPercent(row.delta.drawdownImprovement) : '-'}</td>
            <td class={`px-3 py-2 ${row.delta && Number(row.delta.tradeReduction) >= 0 ? 'text-blue-200' : 'text-red-300'}`}>{row.delta ? `${row.delta.tradeReduction > 0 ? '+' : ''}${row.delta.tradeReduction}` : '-'}</td>
            <td class={`px-3 py-2 ${row.delta && Number(row.delta.whipsawReduction) >= 0 ? 'text-emerald-300' : 'text-red-300'}`}>{row.delta ? `${row.delta.whipsawReduction > 0 ? '+' : ''}${row.delta.whipsawReduction}` : '-'}</td>
            <td class={`px-3 py-2 ${row.delta && Number(row.delta.stabilityImprovement ?? -1) >= 0 ? 'text-cyan-300' : 'text-red-300'}`}>{row.delta && row.delta.stabilityImprovement != null ? formatSignedPercent(row.delta.stabilityImprovement) : '-'}</td>
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
</section>

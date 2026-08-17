<script>
  import {
    factorDrawdownImprovement,
    factorReturnLift,
    factorSourceLabel,
    formatDecimal,
    formatPercent,
    formatSignedPercent
  } from './backtestResearch'

  export let rows = []
  export let insightCards = []
  export let summary = ''
  export let rankingLabel = ''
</script>

<section class="rounded-2xl border border-fuchsia-800/40 bg-fuchsia-950/10 p-5">
  <div class="text-xl font-semibold text-white">팩터 실험실 랭킹</div>
  <div class="mt-2 text-sm text-gray-400">선택한 프리셋별로 최고 성과 시나리오를 뽑아 어떤 팩터 조합이 더 강했는지 바로 정렬합니다. 현재 기준은 <span class="text-fuchsia-200">{rankingLabel}</span> 입니다.</div>
  <div class="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
    {#each insightCards as card}
      <div class="rounded-xl border border-fuchsia-800/40 bg-gray-950/70 p-4">
        <div class="text-xs font-semibold uppercase tracking-wide text-gray-500">{card.label}</div>
        <div class={`mt-2 text-lg font-semibold ${card.accent}`}>{card.headline}</div>
        <div class="mt-1 text-sm text-gray-400">{card.detail}</div>
      </div>
    {/each}
  </div>
  <div class="mt-3 text-sm text-gray-400">{summary}</div>
  <div class="mt-4 overflow-auto">
    <table class="min-w-full text-sm">
      <thead class="text-left text-gray-500">
        <tr>
          <th class="px-3 py-2">순위</th><th class="px-3 py-2">프리셋</th><th class="px-3 py-2">소스</th><th class="px-3 py-2">종목 수</th>
          <th class="px-3 py-2">기준선 수익률</th><th class="px-3 py-2">최고 시나리오</th><th class="px-3 py-2">최고 수익률</th><th class="px-3 py-2">수익 개선</th>
          <th class="px-3 py-2">최고 낙폭</th><th class="px-3 py-2">낙폭 개선</th><th class="px-3 py-2">최고 샤프</th><th class="px-3 py-2">거래 수</th><th class="px-3 py-2">점수</th><th class="px-3 py-2">요약</th>
        </tr>
      </thead>
      <tbody>
        {#each rows as row}
          <tr class={`border-t border-gray-800 ${row.rank === 1 ? 'bg-fuchsia-950/20' : ''}`}>
            <td class="px-3 py-2 font-semibold text-fuchsia-200">#{row.rank}</td>
            <td class="px-3 py-2"><div class="font-medium text-white">{row.label}</div><div class="text-xs text-gray-500">{row.note}</div></td>
            <td class="px-3 py-2 text-gray-300">{factorSourceLabel(row.source)}</td>
            <td class="px-3 py-2 text-gray-300">{row.symbolCount}</td>
            <td class={`px-3 py-2 ${Number(row.baselineReturn ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{formatPercent(row.baselineReturn)}</td>
            <td class="px-3 py-2 text-gray-300">{row.bestScenarioLabel}</td>
            <td class={`px-3 py-2 ${Number(row.bestReturn ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{formatPercent(row.bestReturn)}</td>
            <td class={`px-3 py-2 ${factorReturnLift(row) >= 0 ? 'text-emerald-300' : 'text-red-300'}`}>{formatSignedPercent(factorReturnLift(row))}</td>
            <td class="px-3 py-2 text-red-300">{formatPercent(row.bestDrawdown)}</td>
            <td class={`px-3 py-2 ${factorDrawdownImprovement(row) >= 0 ? 'text-cyan-300' : 'text-red-300'}`}>{formatSignedPercent(factorDrawdownImprovement(row))}</td>
            <td class="px-3 py-2 text-gray-300">{formatDecimal(row.bestSharpe)}</td><td class="px-3 py-2 text-gray-300">{row.bestTrades ?? 0}</td><td class="px-3 py-2 text-fuchsia-200">{formatDecimal(row.bestScore)}</td><td class="px-3 py-2 text-gray-400">{row.summaryTags.join(' · ') || '-'}</td>
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
</section>

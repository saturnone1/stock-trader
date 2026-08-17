<script>
  import { factorSourceLabel, formatDecimal } from './backtestResearch'

  export let summaries = []
</script>

{#if summaries.length > 0}
  <div class="mt-4 overflow-auto rounded-xl border border-gray-800 bg-gray-950">
    <table class="min-w-full text-sm">
      <thead class="text-left text-gray-500">
        <tr>
          <th class="px-3 py-2">프리셋</th><th class="px-3 py-2">소스</th><th class="px-3 py-2">매칭 종목</th><th class="px-3 py-2">실행 여부</th>
          <th class="px-3 py-2">평균 PER</th><th class="px-3 py-2">평균 PBR</th><th class="px-3 py-2">평균 ROE</th><th class="px-3 py-2">흑자 수</th><th class="px-3 py-2">턴어라운드 수</th><th class="px-3 py-2">요약</th>
        </tr>
      </thead>
      <tbody>
        {#each summaries as summary}
          <tr class="border-t border-gray-800">
            <td class="px-3 py-2"><div class="font-medium text-white">{summary.label}</div><div class="text-xs text-gray-500">{summary.note}</div></td>
            <td class="px-3 py-2 text-gray-300">{factorSourceLabel(summary.source)}</td><td class="px-3 py-2 text-gray-300">{summary.matched}</td>
            <td class={`px-3 py-2 ${summary.eligible ? 'text-emerald-300' : 'text-amber-300'}`}>{summary.eligible ? '실행' : '제외'}</td>
            <td class="px-3 py-2 text-gray-300">{formatDecimal(summary.filteredSummary?.averagePe)}</td><td class="px-3 py-2 text-gray-300">{formatDecimal(summary.filteredSummary?.averagePb)}</td>
            <td class="px-3 py-2 text-gray-300">{summary.filteredSummary?.averageRoe != null ? `${formatDecimal(summary.filteredSummary.averageRoe)}%` : '-'}</td>
            <td class="px-3 py-2 text-gray-300">{summary.filteredSummary?.positiveEarningsCount ?? 0}</td><td class="px-3 py-2 text-gray-300">{summary.filteredSummary?.turnaroundCount ?? 0}</td><td class="px-3 py-2 text-gray-400">{summary.summaryTags.join(' · ') || '-'}</td>
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
{/if}

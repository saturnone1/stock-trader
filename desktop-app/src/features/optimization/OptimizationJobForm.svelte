<script>
  import { ChevronDown, Save, SlidersHorizontal, Sparkles } from 'lucide-svelte'
  import { estimatedCombinationCount } from './optimizationModel'

  export let form
  export let patterns = []
  export let timeFrameOptions = []
  export let dataSourceOptions = []
  export let rankOptions = []
  export let entryRuleOptions = []
  export let exitRuleOptions = []
  export let baseline = null
  export let creating = false
  export let loading = false
  export let onPatternChange = () => {}
  export let onFocusChange = () => {}
  export let onCreate = async () => {}

  $: combinations = estimatedCombinationCount(form)

  function formatPercent(value) {
    const number = Number(value)
    return Number.isFinite(number) ? `${number > 0 ? '+' : ''}${(number * 100).toFixed(2)}%` : '-'
  }
</script>

<section class="rounded-2xl border border-gray-800 bg-gray-950 p-6">
  <div class="mb-6 flex items-start gap-3">
    <Sparkles size={20} class="mt-1 text-emerald-300" />
    <div>
      <h3 class="text-xl font-semibold">어느 수치를 다듬을까요?</h3>
      <p class="mt-1 text-sm text-gray-400">한 번에 한 영역만 비교해야 결과의 원인을 해석하기 쉽습니다.</p>
    </div>
  </div>

  {#if baseline}
    <div class="mb-6 grid grid-cols-2 gap-3 rounded-xl border border-blue-900/60 bg-blue-950/20 p-4 text-sm lg:grid-cols-4">
      <div><div class="text-xs text-gray-500">기준 수익률</div><div class="mt-1 font-semibold text-blue-100">{formatPercent(baseline.totalReturn)}</div></div>
      <div><div class="text-xs text-gray-500">기준 최대 낙폭</div><div class="mt-1 font-semibold text-red-200">{formatPercent(baseline.maxDrawdown)}</div></div>
      <div><div class="text-xs text-gray-500">기준 거래 수</div><div class="mt-1 font-semibold">{baseline.tradeCount ?? '-'}</div></div>
      <div><div class="text-xs text-gray-500">기준 소르티노</div><div class="mt-1 font-semibold">{Number(baseline.sortinoRatio ?? 0).toFixed(2)}</div></div>
    </div>
  {/if}

  <div class="grid grid-cols-1 gap-4 xl:grid-cols-5">
    <label class="text-sm text-gray-300 xl:col-span-2">
      <div class="mb-2 text-gray-500">검증할 전략</div>
      <select bind:value={form.patternId} on:change={onPatternChange} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
        {#each patterns as pattern}<option value={pattern.id}>{pattern.name}</option>{/each}
      </select>
    </label>
    <label class="text-sm text-gray-300 xl:col-span-3"><div class="mb-2 text-gray-500">종목</div><input bind:value={form.symbolsText} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="SPY, QQQ, TQQQ" /></label>
    <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">시작일</div><input type="date" bind:value={form.from} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
    <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">종료일</div><input type="date" bind:value={form.to} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
    <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">봉 단위</div><select bind:value={form.timeFrame} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">{#each timeFrameOptions as [value, label]}<option value={value}>{label}</option>{/each}</select></label>
    <label class="text-sm text-gray-300 xl:col-span-2"><div class="mb-2 text-gray-500">데이터 소스</div><select bind:value={form.dataSource} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">{#each dataSourceOptions as [value, label]}<option value={value}>{label}</option>{/each}</select></label>
  </div>

  <div class="mt-7 grid grid-cols-1 gap-3 lg:grid-cols-3">
    <button type="button" on:click={() => onFocusChange('entry')} disabled={!entryRuleOptions.length} class={`rounded-xl border p-5 text-left transition disabled:cursor-not-allowed disabled:opacity-40 ${form.tuningFocus === 'entry' ? 'border-emerald-500 bg-emerald-950/30' : 'border-gray-800 bg-gray-900 hover:border-gray-700'}`}><div class="font-semibold text-white">진입 시점</div><div class="mt-2 text-sm text-gray-400">매수 조건의 기간값만 비교합니다.</div></button>
    <button type="button" on:click={() => onFocusChange('exit')} disabled={!exitRuleOptions.length} class={`rounded-xl border p-5 text-left transition disabled:cursor-not-allowed disabled:opacity-40 ${form.tuningFocus === 'exit' ? 'border-emerald-500 bg-emerald-950/30' : 'border-gray-800 bg-gray-900 hover:border-gray-700'}`}><div class="font-semibold text-white">청산 시점</div><div class="mt-2 text-sm text-gray-400">매도 조건의 기간값만 비교합니다.</div></button>
    <button type="button" on:click={() => onFocusChange('risk')} class={`rounded-xl border p-5 text-left transition ${form.tuningFocus === 'risk' ? 'border-emerald-500 bg-emerald-950/30' : 'border-gray-800 bg-gray-900 hover:border-gray-700'}`}><div class="font-semibold text-white">손절·목표</div><div class="mt-2 text-sm text-gray-400">ATR 손절과 목표 배수만 비교합니다.</div></button>
  </div>

  {#if form.tuningFocus === 'entry'}
    <div class="mt-6 grid grid-cols-1 gap-4 rounded-xl border border-emerald-900/60 bg-emerald-950/10 p-5 lg:grid-cols-2">
      <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">다듬을 매수 조건</div><select bind:value={form.selectedEntryRuleIndex} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">{#each entryRuleOptions as option}<option value={option.index}>{option.label}</option>{/each}</select></label>
      <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">비교할 기간값</div><input bind:value={form.entryPeriodValuesText} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="10, 20, 30" /><div class="mt-2 text-xs text-gray-500">현재값 주변의 3~5개 값만 권장합니다.</div></label>
    </div>
  {:else if form.tuningFocus === 'exit'}
    <div class="mt-6 grid grid-cols-1 gap-4 rounded-xl border border-emerald-900/60 bg-emerald-950/10 p-5 lg:grid-cols-2">
      <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">다듬을 매도 조건</div><select bind:value={form.selectedExitRuleIndex} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">{#each exitRuleOptions as option}<option value={option.index}>{option.label}</option>{/each}</select></label>
      <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">비교할 기간값</div><input bind:value={form.exitPeriodValuesText} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="5, 10, 20" /><div class="mt-2 text-xs text-gray-500">청산이 지나치게 빠르거나 늦지 않은지 비교합니다.</div></label>
    </div>
  {:else}
    <div class="mt-6 rounded-xl border border-emerald-900/60 bg-emerald-950/10 p-5">
      <div class="mb-4 text-sm font-semibold text-emerald-100">최소값 · 최대값 · 간격</div>
      <div class="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <div><div class="mb-2 text-sm text-gray-400">ATR 손절 배수</div><div class="grid grid-cols-3 gap-2"><input aria-label="손절 최소" type="number" step="0.1" bind:value={form.atrStopMin} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /><input aria-label="손절 최대" type="number" step="0.1" bind:value={form.atrStopMax} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /><input aria-label="손절 간격" type="number" step="0.1" bind:value={form.atrStopStep} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div></div>
        <div><div class="mb-2 text-sm text-gray-400">ATR 목표 배수</div><div class="grid grid-cols-3 gap-2"><input aria-label="목표 최소" type="number" step="0.1" bind:value={form.atrTargetMin} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /><input aria-label="목표 최대" type="number" step="0.1" bind:value={form.atrTargetMax} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /><input aria-label="목표 간격" type="number" step="0.1" bind:value={form.atrTargetStep} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div></div>
      </div>
    </div>
  {/if}

  <div class="mt-6 flex flex-wrap items-center justify-between gap-4 rounded-xl border border-blue-900/60 bg-blue-950/20 p-4"><div><div class="text-xs text-gray-500">예상 비교 조합</div><div class="mt-1 text-xl font-bold text-blue-100">{combinations.toLocaleString()}개</div></div><div class="max-w-xl text-sm text-blue-100/70">최고 수익률 하나가 아니라 기준 전략 대비 낙폭, OOS 성과와 거래 수까지 함께 비교합니다.</div></div>

  <details class="mt-6 rounded-xl border border-gray-800 bg-gray-900 p-4">
    <summary class="flex cursor-pointer list-none items-center gap-2 text-sm text-gray-300"><SlidersHorizontal size={16} /> 전문가 실행 설정 <ChevronDown size={15} /></summary>
    <div class="mt-5 grid grid-cols-2 gap-4 lg:grid-cols-4">
      <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">결과 순위 기준</div><select bind:value={form.rankBy} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">{#each rankOptions as [value, label]}<option value={value}>{label}</option>{/each}</select></label>
      <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">표시 후보 수</div><input type="number" bind:value={form.maxResults} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" /></label>
      <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">최대 조합 수</div><input type="number" bind:value={form.maxCombinations} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" /></label>
      <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">검증 구간 비율</div><input type="number" step="0.05" min="0.1" max="0.5" bind:value={form.oosPercent} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" /></label>
      <label class="text-sm text-gray-300 lg:col-span-2"><div class="mb-2 text-gray-500">작업 이름</div><input bind:value={form.jobName} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" /></label>
      <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">작업 우선순위</div><input type="number" bind:value={form.priority} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" /></label>
      <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">계산 묶음 크기</div><input type="number" bind:value={form.chunkSize} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" /></label>
    </div>
  </details>

  <div class="mt-6 flex justify-end"><button on:click={onCreate} disabled={creating || loading || combinations < 1} class="flex items-center gap-2 rounded bg-emerald-600 px-5 py-3 text-sm font-semibold text-white transition hover:bg-emerald-700 disabled:opacity-50"><Save size={16} />{creating ? '후보 생성 중...' : '후보 찾기 시작'}</button></div>
</section>

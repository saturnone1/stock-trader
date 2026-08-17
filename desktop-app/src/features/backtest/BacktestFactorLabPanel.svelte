<script>
  import BacktestFactorCandidates from './BacktestFactorCandidates.svelte'
  import BacktestFactorExperimentEditor from './BacktestFactorExperimentEditor.svelte'

  export let factorLab
  export let loading = false
  export let error = ''
  export let summaries = []
  export let presets = []
  export let rankingOptions = []
  export let baseSymbolCount = 0
  export let selectionCount = 0
  export let onPreview
  export let onTogglePreset
  export let onAddCustom
  export let onRemoveCustom
</script>

<div class="mt-5 rounded-xl border border-fuchsia-800/40 bg-fuchsia-950/20 p-5">
  <div class="flex items-start justify-between gap-4">
    <div>
      <div class="text-sm font-semibold text-fuchsia-100">팩터 실험실</div>
      <div class="mt-1 text-sm text-fuchsia-50">현재 종목 입력을 기준으로 여러 재무 팩터 조합을 자동 생성하고, 같은 타이밍 시나리오로 돌린 뒤 어떤 조합이 더 나았는지 랭킹합니다.</div>
    </div>
    <div class="flex items-center gap-3">
      <label class="flex items-center gap-2 text-sm text-fuchsia-100"><input type="checkbox" bind:checked={factorLab.enabled} />실험실 사용</label>
      <button on:click={onPreview} disabled={!factorLab.enabled || loading || baseSymbolCount === 0 || selectionCount === 0} class="rounded bg-fuchsia-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-fuchsia-700 disabled:opacity-40">{loading ? '불러오는 중...' : '후보 미리보기'}</button>
    </div>
  </div>

  {#if error}<div class="mt-4 rounded-lg border border-red-700 bg-red-900/20 p-4 text-sm text-red-300">{error}</div>{/if}

  <div class="mt-4 grid grid-cols-1 gap-3 xl:grid-cols-3">
    <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300"><div class="text-xs text-gray-500">기준 종목군</div><div class="mt-2 text-xl font-semibold text-white">{baseSymbolCount}</div></div>
    <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300"><div class="text-xs text-gray-500">선택한 실험 조합</div><div class="mt-2 text-xl font-semibold text-white">{selectionCount}</div></div>
    <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300"><div class="text-xs text-gray-500">실행 가능한 프리셋</div><div class="mt-2 text-xl font-semibold text-white">{summaries.filter((item) => item.eligible).length}</div></div>
  </div>

  <div class="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-4">
    <label class="flex items-center gap-2 rounded border border-fuchsia-800/30 bg-gray-950 px-4 py-3 text-sm text-gray-300"><input type="checkbox" bind:checked={factorLab.includeCurrentBuilder} disabled={!factorLab.enabled} />현재 재무 팩터 빌더 조건 포함</label>
    <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">최소 종목 수</div><input type="number" min="1" max="1000" bind:value={factorLab.minMatchedSymbols} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" disabled={!factorLab.enabled} /></label>
    <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">랭킹 기준</div><select bind:value={factorLab.rankingMode} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" disabled={!factorLab.enabled}>{#each rankingOptions as option}<option value={option.id}>{option.label}</option>{/each}</select></label>
    <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">랭킹 표시 개수</div><input type="number" min="1" max="20" bind:value={factorLab.topRankedResults} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" disabled={!factorLab.enabled} /></label>
  </div>

  <div class="mt-4 grid grid-cols-1 gap-3 xl:grid-cols-3">
    {#each presets as preset}
      <label class={`rounded-xl border px-4 py-3 text-sm transition ${factorLab.selectedPresets.includes(preset.id) ? 'border-fuchsia-500 bg-fuchsia-950/30 text-fuchsia-50' : 'border-gray-800 bg-gray-950 text-gray-300'}`}>
        <div class="flex items-start gap-3"><input type="checkbox" checked={factorLab.selectedPresets.includes(preset.id)} on:change={() => onTogglePreset(preset.id)} disabled={!factorLab.enabled} class="mt-1" /><div><div class="font-medium text-white">{preset.label}</div><div class="mt-1 text-xs text-gray-400">{preset.note}</div></div></div>
      </label>
    {/each}
  </div>

  <BacktestFactorExperimentEditor {factorLab} onAdd={onAddCustom} onRemove={onRemoveCustom} />
  <BacktestFactorCandidates {summaries} />
</div>

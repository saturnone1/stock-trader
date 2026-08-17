<script>
  export let factorLab
  export let onAdd
  export let onRemove
</script>

<div class="mt-4 rounded-xl border border-fuchsia-800/30 bg-gray-950 p-4">
  <div class="mb-3 flex items-center justify-between gap-4">
    <div>
      <div class="text-sm font-semibold text-white">커스텀 팩터 조합</div>
      <div class="mt-1 text-xs text-gray-400">PER/PBR/ROE/마진/성장/턴어라운드 조건을 직접 섞어서 배치 실험용 조합을 여러 개 만듭니다.</div>
    </div>
    <button on:click={onAdd} disabled={!factorLab.enabled} class="rounded bg-fuchsia-700 px-3 py-2 text-sm font-medium text-white transition hover:bg-fuchsia-600 disabled:opacity-40">커스텀 조합 추가</button>
  </div>

  <div class="space-y-4">
    {#each factorLab.customExperiments as experiment, index (experiment.id)}
      <div class="rounded-lg border border-gray-800 bg-gray-900 p-4">
        <div class="mb-3 flex items-center justify-between gap-4">
          <label class="flex-1 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">조합 이름</div>
            <input bind:value={experiment.label} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder={`커스텀 조합 ${index + 1}`} />
          </label>
          <button on:click={() => onRemove(experiment.id)} disabled={!factorLab.enabled || factorLab.customExperiments.length === 1} class="mt-6 rounded border border-red-700 px-3 py-2 text-sm text-red-300 transition hover:bg-red-950/30 disabled:opacity-40">제거</button>
        </div>

        <div class="grid grid-cols-1 gap-3 xl:grid-cols-3">
          <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">PER 최대</div><input bind:value={experiment.peRatioMax} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder="예: 15" /></label>
          <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">PBR 최대</div><input bind:value={experiment.pbRatioMax} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder="예: 1.5" /></label>
          <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">ROE 최소 (%)</div><input bind:value={experiment.roePercentMin} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder="예: 12" /></label>
          <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">영업이익률 최소 (%)</div><input bind:value={experiment.operatingMarginMin} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder="예: 8" /></label>
          <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">매출 성장 최소</div><input bind:value={experiment.revenueGrowthMin} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder="예: 0.1" /></label>
          <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">순이익 성장 최소</div><input bind:value={experiment.netIncomeGrowthMin} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder="예: 0.15" /></label>
        </div>

        <div class="mt-3 grid grid-cols-1 gap-3 xl:grid-cols-2">
          <label class="flex items-center gap-2 rounded border border-gray-800 bg-gray-950 px-4 py-3 text-sm text-gray-300"><input type="checkbox" bind:checked={experiment.positiveEarningsOnly} disabled={!factorLab.enabled} />흑자 기업만</label>
          <label class="flex items-center gap-2 rounded border border-gray-800 bg-gray-950 px-4 py-3 text-sm text-gray-300"><input type="checkbox" bind:checked={experiment.turnaroundOnly} disabled={!factorLab.enabled} />턴어라운드만</label>
        </div>
      </div>
    {/each}
  </div>
</div>

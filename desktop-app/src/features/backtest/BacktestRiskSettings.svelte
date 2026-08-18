<script>
  export let form
</script>

<div class="mt-6 grid grid-cols-1 gap-4 xl:grid-cols-2">
  <div class="rounded-xl border border-gray-800 bg-gray-900 p-5">
    <div class="mb-4 text-sm font-semibold text-white">고급 분석</div>
    <div class="grid grid-cols-1 gap-4 xl:grid-cols-2">
      <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
        <label class="mb-3 flex items-center gap-2 text-sm text-gray-300"><input type="checkbox" bind:checked={form.enableWalkForward} />워크포워드 분석</label>
        <div class="grid grid-cols-2 gap-2">
          <label class="text-xs text-gray-500">학습 구간(개월)<input type="number" min="1" step="1" bind:value={form.walkForwardInSampleMonths} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" disabled={!form.enableWalkForward} /></label>
          <label class="text-xs text-gray-500">검증 구간(개월)<input type="number" min="1" step="1" bind:value={form.walkForwardOutOfSampleMonths} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" disabled={!form.enableWalkForward} /></label>
        </div>
        <p class="mt-2 text-xs text-gray-600">학습과 검증 구간은 날짜가 겹치지 않으며, 완전한 구간만 계산합니다.</p>
      </div>
      <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
        <label class="mb-3 flex items-center gap-2 text-sm text-gray-300"><input type="checkbox" bind:checked={form.enableMonteCarlo} />몬테카를로 시뮬레이션</label>
        <input type="number" bind:value={form.monteCarloSimulations} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="시뮬레이션 횟수" disabled={!form.enableMonteCarlo} />
      </div>
    </div>
  </div>

  <div class="rounded-xl border border-gray-800 bg-gray-900 p-5">
    <div class="mb-4 text-sm font-semibold text-white">리스크 관리</div>
    <div class="grid grid-cols-2 gap-3 text-sm">
      <label class="text-gray-300"><div class="mb-2 text-gray-500">거래당 리스크</div><input type="number" step="0.001" bind:value={form.riskPerTradePercent} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" /></label>
      <label class="text-gray-300"><div class="mb-2 text-gray-500">일일 손실 한도</div><input type="number" step="0.005" bind:value={form.dailyLossLimitPercent} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" /></label>
      <label class="text-gray-300"><div class="mb-2 text-gray-500">전체 최대 포지션</div><input type="number" bind:value={form.maxTotalPositions} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" /></label>
      <label class="text-gray-300"><div class="mb-2 text-gray-500">섹터당 최대 포지션</div><input type="number" bind:value={form.maxPositionsPerSector} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" /></label>
    </div>
  </div>
</div>

<div class="mt-6 rounded-xl border border-gray-800 bg-gray-900 p-5">
  <div class="mb-4 flex items-center justify-between"><div class="text-sm font-semibold text-white">포트폴리오 비중 전략</div><label class="flex items-center gap-2 text-sm text-gray-300"><input type="checkbox" bind:checked={form.useWeightStrategy} />사용</label></div>
  <div class="grid grid-cols-2 gap-3 xl:grid-cols-6">
    <input type="number" step="0.05" bind:value={form.bullWeight} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="강세 가중치" disabled={!form.useWeightStrategy} /><input type="number" step="0.05" bind:value={form.bearWeight} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="약세 가중치" disabled={!form.useWeightStrategy} />
    <input type="number" step="0.05" bind:value={form.overheat1Weight} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="과열1 가중치" disabled={!form.useWeightStrategy} /><input type="number" step="0.05" bind:value={form.overheat2Weight} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="과열2 가중치" disabled={!form.useWeightStrategy} />
    <input type="number" step="0.01" bind:value={form.overheatStage1Pct} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="과열1 임계" disabled={!form.useWeightStrategy} /><input type="number" step="0.01" bind:value={form.overheatStage2Pct} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="과열2 임계" disabled={!form.useWeightStrategy} />
  </div>
  <div class="mt-3 max-w-xs"><input type="number" bind:value={form.smaPeriod} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="SMA 기간" disabled={!form.useWeightStrategy} /></div>
</div>

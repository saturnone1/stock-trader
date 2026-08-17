<script>
  export let timingEnabled = false
  export let universeComparison
  export let currentCount = 0
  export let universeCount = 0
  export let financialCount = 0
  export let combinedCount = 0
  export let universeMatched = 0
  export let financialMatched = 0
</script>

<div class="mt-5 rounded-xl border border-emerald-800/40 bg-emerald-950/20 p-5">
  <div class="flex items-center justify-between gap-4">
    <div>
      <div class="text-sm font-semibold text-emerald-100">유니버스·팩터 비교</div>
      <div class="mt-1 text-sm text-emerald-50">현재 입력 종목군에서 시총/섹터 필터, 재무 팩터 필터, 교집합 필터를 직접 잘라 같은 타이밍 시나리오로 비교합니다.</div>
    </div>
    <label class="flex items-center gap-2 text-sm text-emerald-100"><input type="checkbox" bind:checked={universeComparison.enabled} disabled={!timingEnabled} />필터 비교 포함</label>
  </div>

  <div class="mt-4 grid grid-cols-1 gap-3 xl:grid-cols-4">
    <label class="flex items-center gap-2 rounded border border-emerald-800/40 bg-gray-950 px-4 py-3 text-sm text-gray-300"><input type="checkbox" bind:checked={universeComparison.includeCurrentSymbols} disabled={!timingEnabled} />현재 입력 유지</label>
    <label class="flex items-center gap-2 rounded border border-emerald-800/40 bg-gray-950 px-4 py-3 text-sm text-gray-300"><input type="checkbox" bind:checked={universeComparison.includeUniverseBuilder} disabled={!timingEnabled || !universeComparison.enabled} />시총·섹터 필터</label>
    <label class="flex items-center gap-2 rounded border border-emerald-800/40 bg-gray-950 px-4 py-3 text-sm text-gray-300"><input type="checkbox" bind:checked={universeComparison.includeFinancialFactor} disabled={!timingEnabled || !universeComparison.enabled} />재무 팩터 필터</label>
    <label class="flex items-center gap-2 rounded border border-emerald-800/40 bg-gray-950 px-4 py-3 text-sm text-gray-300"><input type="checkbox" bind:checked={universeComparison.includeCombined} disabled={!timingEnabled || !universeComparison.enabled} />교집합 필터</label>
  </div>

  <div class="mt-4 grid grid-cols-1 gap-3 xl:grid-cols-4">
    <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300"><div class="text-xs text-gray-500">현재 입력</div><div class="mt-2 text-xl font-semibold text-white">{currentCount}</div></div>
    <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300"><div class="text-xs text-gray-500">시총·섹터 필터 후</div><div class="mt-2 text-xl font-semibold text-white">{universeCount}</div><div class="mt-1 text-xs text-gray-500">후보 {universeMatched}개</div></div>
    <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300"><div class="text-xs text-gray-500">재무 팩터 후</div><div class="mt-2 text-xl font-semibold text-white">{financialCount}</div><div class="mt-1 text-xs text-gray-500">후보 {financialMatched}개</div></div>
    <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300"><div class="text-xs text-gray-500">교집합 필터 후</div><div class="mt-2 text-xl font-semibold text-white">{combinedCount}</div><div class="mt-1 text-xs text-gray-500">타이밍 비교에 바로 투입</div></div>
  </div>
</div>

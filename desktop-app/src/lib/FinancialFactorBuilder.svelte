<script>
  import { onMount } from 'svelte'
  import { financialFactorApi } from '../api/endpoints'

  export let symbolsText = ''
  export let title = '재무 팩터 빌더'
  export let description = '저PER, 저PBR, ROE, 턴어라운드, 재무 성장 조건으로 종목군을 다시 좁힙니다.'
  export let candidateSymbols = []
  export let selectionSummary = null
  export let filterParams = null

  const samplePayload = JSON.stringify([
    {
      symbol: 'AAPL',
      asOfDate: '2026-03-31',
      source: 'Manual',
      peRatio: 24.1,
      pbRatio: 7.9,
      roePercent: 31.2,
      operatingMarginPercent: 30.5,
      revenueCurrent: 90753,
      revenuePrevious: 85777,
      operatingIncomeCurrent: 29591,
      operatingIncomePrevious: 27900,
      netIncomeCurrent: 23636,
      netIncomePrevious: 21448
    }
  ], null, 2)

  let meta = {
    totalSnapshots: 0,
    symbolsCovered: 0,
    latestAsOfDate: null,
    coverage: { peRatio: 0, pbRatio: 0, roePercent: 0, revenueGrowth: 0, netIncomeGrowth: 0, turnaround: 0 }
  }
  let result = {
    totalUniverse: 0,
    matched: 0,
    items: [],
    comparison: {
      overall: { count: 0, positiveEarningsCount: 0, turnaroundCount: 0 },
      filtered: { count: 0, positiveEarningsCount: 0, turnaroundCount: 0 }
    }
  }
  let loading = true
  let importing = false
  let runningPipeline = false
  let runningVendorSync = false
  let error = ''
  let externalSyncSymbols = ''
  let pipeline = {
    enabled: false,
    importDirectory: '',
    scanIntervalMinutes: 0,
    latestSuccessAt: null,
    vendorSync: {
      enabled: false,
      provider: 'SEC',
      syncIntervalHours: 24,
      symbolLimit: 50,
      configuredSymbolCount: 0,
      configuredSymbols: [],
      latestSuccessAt: null
    },
    recentRuns: []
  }
  let filters = {
    peRatioMax: '15',
    pbRatioMax: '',
    roePercentMin: '',
    operatingMarginMin: '',
    revenueGrowthMin: '',
    netIncomeGrowthMin: '',
    turnaroundOnly: false,
    positiveEarningsOnly: true,
    sortBy: 'peAsc',
    limit: 20
  }
  let importPayload = samplePayload

  onMount(async () => {
    await Promise.all([loadMeta(), queryFactors(), loadPipelineStatus()])
    loading = false
  })

  async function loadMeta() {
    try {
      const response = await financialFactorApi.meta()
      meta = response.data ?? meta
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '재무 팩터 메타 정보를 불러오지 못했습니다.'
    }
  }

  async function queryFactors() {
    try {
      const response = await financialFactorApi.query({
        peRatioMax: filters.peRatioMax || undefined,
        pbRatioMax: filters.pbRatioMax || undefined,
        roePercentMin: filters.roePercentMin || undefined,
        operatingMarginMin: filters.operatingMarginMin || undefined,
        revenueGrowthMin: filters.revenueGrowthMin || undefined,
        netIncomeGrowthMin: filters.netIncomeGrowthMin || undefined,
        turnaroundOnly: filters.turnaroundOnly || undefined,
        positiveEarningsOnly: filters.positiveEarningsOnly || undefined,
        limit: filters.limit,
        sortBy: filters.sortBy
      })
      result = response.data ?? result
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '재무 팩터 조회에 실패했습니다.'
    }
  }

  async function loadPipelineStatus() {
    try {
      const response = await financialFactorApi.pipelineStatus()
      pipeline = response.data ?? pipeline
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '재무 파이프라인 상태를 불러오지 못했습니다.'
    }
  }

  async function importSnapshots() {
    importing = true
    try {
      const parsed = JSON.parse(importPayload)
      const payload = Array.isArray(parsed) ? parsed : [parsed]
      await financialFactorApi.import(payload)
      await Promise.all([loadMeta(), queryFactors(), loadPipelineStatus()])
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '재무 데이터 업로드에 실패했습니다.'
    } finally {
      importing = false
    }
  }

  function replaceSymbols() {
    symbolsText = result.items.map((item) => item.symbol).join(', ')
  }

  async function runPipelineNow() {
    runningPipeline = true
    try {
      await financialFactorApi.runPipeline()
      await Promise.all([loadMeta(), queryFactors(), loadPipelineStatus()])
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '재무 파이프라인 실행에 실패했습니다.'
    } finally {
      runningPipeline = false
    }
  }

  async function runVendorSyncNow() {
    runningVendorSync = true
    try {
      const symbols = (externalSyncSymbols || symbolsText || '').trim()
      await financialFactorApi.runVendorSync(symbols || undefined)
      await Promise.all([loadMeta(), queryFactors(), loadPipelineStatus()])
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '외부 재무 벤더 동기화에 실패했습니다.'
    } finally {
      runningVendorSync = false
    }
  }

  function pct(value, digits = 1) {
    if (value == null || Number.isNaN(Number(value))) return '-'
    return `${(Number(value) * 100).toFixed(digits)}%`
  }

  function num(value, digits = 2) {
    if (value == null || Number.isNaN(Number(value))) return '-'
    return Number(value).toFixed(digits)
  }

  function marketCap(value) {
    const numeric = Number(value ?? 0)
    if (!numeric) return '-'
    if (numeric >= 1_000_000_000_000) return `${(numeric / 1_000_000_000_000).toFixed(2)}T`
    if (numeric >= 1_000_000_000) return `${(numeric / 1_000_000_000).toFixed(2)}B`
    if (numeric >= 1_000_000) return `${(numeric / 1_000_000).toFixed(2)}M`
    return numeric.toLocaleString('en-US', { maximumFractionDigits: 0 })
  }

  function formatDateTime(value) {
    if (!value) return '-'
    return new Date(value).toLocaleString('ko-KR')
  }

  $: candidateSymbols = result.items.map((item) => item.symbol)
  $: selectionSummary = {
    totalUniverse: result.totalUniverse,
    matched: result.matched,
    previewCount: result.items.length,
    positiveEarningsOnly: filters.positiveEarningsOnly,
    turnaroundOnly: filters.turnaroundOnly,
    peRatioMax: filters.peRatioMax,
    pbRatioMax: filters.pbRatioMax,
    roePercentMin: filters.roePercentMin,
    revenueGrowthMin: filters.revenueGrowthMin,
    netIncomeGrowthMin: filters.netIncomeGrowthMin
  }
  $: filterParams = { ...filters }
</script>

<section class="rounded-2xl border border-emerald-800/50 bg-emerald-950/20 p-5">
  <div class="flex items-start justify-between gap-4">
    <div>
      <h3 class="text-base font-semibold text-emerald-100">{title}</h3>
      <p class="mt-1 text-sm text-emerald-50">{description}</p>
      <p class="mt-2 text-xs text-emerald-200">이 레이어는 업로드된 최신 재무 스냅샷을 기준으로 필터링합니다.</p>
    </div>
    <button on:click={replaceSymbols} disabled={result.items.length === 0} class="rounded bg-emerald-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-emerald-700 disabled:opacity-40">
      종목 입력에 반영
    </button>
  </div>

  {#if error}
    <div class="mt-4 rounded-lg border border-red-700 bg-red-900/20 p-4 text-sm text-red-300">{error}</div>
  {/if}

  <div class="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-4">
    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
      <div class="text-xs text-gray-500">스냅샷 수</div>
      <div class="mt-2 text-2xl font-bold">{meta.totalSnapshots}</div>
    </div>
    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
      <div class="text-xs text-gray-500">커버 종목</div>
      <div class="mt-2 text-2xl font-bold">{meta.symbolsCovered}</div>
    </div>
    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
      <div class="text-xs text-gray-500">최근 기준일</div>
      <div class="mt-2 text-lg font-bold">{meta.latestAsOfDate ?? '-'}</div>
    </div>
    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
      <div class="text-xs text-gray-500">현재 매칭</div>
      <div class="mt-2 text-2xl font-bold">{result.matched}</div>
    </div>
  </div>

  <div class="mt-4 rounded-xl border border-cyan-800/50 bg-cyan-950/20 p-4">
    <div class="flex items-start justify-between gap-4">
      <div>
        <div class="text-sm font-semibold text-cyan-100">자동 수집 파이프라인</div>
        <div class="mt-1 text-sm text-cyan-50">`{pipeline.importDirectory || '-'}` 폴더의 JSON/CSV를 주기적으로 스캔해 재무 스냅샷을 자동 반영합니다.</div>
        <div class="mt-2 text-xs text-cyan-200">주기 {pipeline.scanIntervalMinutes}분 · 최근 성공 {formatDateTime(pipeline.latestSuccessAt)}</div>
      </div>
      <button on:click={runPipelineNow} disabled={runningPipeline || !pipeline.enabled} class="rounded bg-cyan-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-cyan-700 disabled:opacity-40">
        {runningPipeline ? '실행 중...' : '지금 스캔'}
      </button>
    </div>

    <div class="mt-4 overflow-auto rounded-lg border border-gray-800 bg-gray-950">
      <table class="min-w-full text-sm">
        <thead class="text-left text-gray-500">
          <tr>
            <th class="px-3 py-2">시작</th>
            <th class="px-3 py-2">타입</th>
            <th class="px-3 py-2">상태</th>
            <th class="px-3 py-2">반영</th>
            <th class="px-3 py-2">파일</th>
          </tr>
        </thead>
        <tbody>
          {#if pipeline.recentRuns.length === 0}
            <tr class="border-t border-gray-800">
              <td colspan="5" class="px-3 py-4 text-center text-sm text-gray-400">아직 파이프라인 실행 이력이 없습니다.</td>
            </tr>
          {:else}
            {#each pipeline.recentRuns as run}
              <tr class="border-t border-gray-800">
                <td class="px-3 py-2 text-gray-300">{formatDateTime(run.startedAt)}</td>
                <td class="px-3 py-2 text-gray-300">{run.sourceType}</td>
                <td class="px-3 py-2 text-gray-300">{run.status}</td>
                <td class="px-3 py-2 text-gray-300">{run.importedCount}</td>
                <td class="px-3 py-2 text-gray-400">{run.filePath}</td>
              </tr>
            {/each}
          {/if}
        </tbody>
      </table>
    </div>
  </div>

  <div class="mt-4 rounded-xl border border-violet-800/50 bg-violet-950/20 p-4">
    <div class="flex items-start justify-between gap-4">
      <div>
        <div class="text-sm font-semibold text-violet-100">외부 재무 벤더 동기화</div>
        <div class="mt-1 text-sm text-violet-50">SEC 공시 데이터를 기준으로 최신 연간 재무 스냅샷을 가져와 파일 업로드 없이 바로 팩터 레이어를 갱신합니다.</div>
        <div class="mt-2 text-xs text-violet-200">
          공급자 {pipeline.vendorSync.provider} · 자동 주기 {pipeline.vendorSync.syncIntervalHours}시간 · 최근 성공 {formatDateTime(pipeline.vendorSync.latestSuccessAt)}
        </div>
        <div class="mt-1 text-xs text-violet-200">
          설정 심볼 {pipeline.vendorSync.configuredSymbolCount}개 · 기본 상한 {pipeline.vendorSync.symbolLimit}개
        </div>
      </div>
      <button on:click={runVendorSyncNow} disabled={runningVendorSync} class="rounded bg-violet-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-violet-700 disabled:opacity-40">
        {runningVendorSync ? '동기화 중...' : '외부 동기화'}
      </button>
    </div>

    <div class="mt-4 grid gap-3 xl:grid-cols-[1fr_auto]">
      <label class="text-sm text-gray-300">
        <div class="mb-2 text-gray-500">동기화 심볼</div>
        <input bind:value={externalSyncSymbols} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="비워두면 설정 심볼 또는 상위 활성 종목을 사용합니다." />
      </label>
      <button on:click={() => { externalSyncSymbols = symbolsText }} class="mt-6 rounded border border-violet-700 px-3 py-2 text-sm text-violet-200 transition hover:bg-violet-950/30">
        현재 종목 입력 불러오기
      </button>
    </div>

    {#if pipeline.vendorSync.configuredSymbols.length > 0}
      <div class="mt-3 text-xs text-violet-200">설정 미리보기: {pipeline.vendorSync.configuredSymbols.join(', ')}</div>
    {/if}
  </div>

  <div class="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-3">
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">PER 최대</div>
      <input bind:value={filters.peRatioMax} on:change={queryFactors} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="예: 15" />
    </label>
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">PBR 최대</div>
      <input bind:value={filters.pbRatioMax} on:change={queryFactors} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="예: 1.5" />
    </label>
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">ROE 최소 (%)</div>
      <input bind:value={filters.roePercentMin} on:change={queryFactors} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="예: 10" />
    </label>
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">영업이익률 최소 (%)</div>
      <input bind:value={filters.operatingMarginMin} on:change={queryFactors} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="예: 8" />
    </label>
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">매출 성장 최소</div>
      <input bind:value={filters.revenueGrowthMin} on:change={queryFactors} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="예: 0.1 = 10%" />
    </label>
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">순이익 성장 최소</div>
      <input bind:value={filters.netIncomeGrowthMin} on:change={queryFactors} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="예: 0.2 = 20%" />
    </label>
  </div>

  <div class="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-4">
    <label class="flex items-center gap-2 rounded-xl border border-gray-800 bg-gray-950 px-4 py-3 text-sm text-gray-300">
      <input type="checkbox" bind:checked={filters.positiveEarningsOnly} on:change={queryFactors} />
      흑자 기업만
    </label>
    <label class="flex items-center gap-2 rounded-xl border border-gray-800 bg-gray-950 px-4 py-3 text-sm text-gray-300">
      <input type="checkbox" bind:checked={filters.turnaroundOnly} on:change={queryFactors} />
      턴어라운드만
    </label>
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">정렬</div>
      <select bind:value={filters.sortBy} on:change={queryFactors} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
        <option value="peAsc">PER 낮은 순</option>
        <option value="pbAsc">PBR 낮은 순</option>
        <option value="roeDesc">ROE 높은 순</option>
        <option value="revenueGrowthDesc">매출 성장 높은 순</option>
        <option value="netIncomeGrowthDesc">순이익 성장 높은 순</option>
      </select>
    </label>
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">미리보기 수</div>
      <input type="number" min="1" max="100" bind:value={filters.limit} on:change={queryFactors} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
    </label>
  </div>

  <div class="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-2">
    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
      <div class="mb-3 text-sm font-semibold text-white">필터 전/후 비교</div>
      <div class="grid grid-cols-2 gap-3 text-sm">
        <div class="rounded border border-gray-800 bg-gray-900 p-3">전체 평균 PER {num(result.comparison.overall.averagePe)}</div>
        <div class="rounded border border-gray-800 bg-gray-900 p-3">필터 후 평균 PER {num(result.comparison.filtered.averagePe)}</div>
        <div class="rounded border border-gray-800 bg-gray-900 p-3">전체 평균 PBR {num(result.comparison.overall.averagePb)}</div>
        <div class="rounded border border-gray-800 bg-gray-900 p-3">필터 후 평균 PBR {num(result.comparison.filtered.averagePb)}</div>
        <div class="rounded border border-gray-800 bg-gray-900 p-3">전체 흑자 수 {result.comparison.overall.positiveEarningsCount}</div>
        <div class="rounded border border-gray-800 bg-gray-900 p-3">필터 후 흑자 수 {result.comparison.filtered.positiveEarningsCount}</div>
        <div class="rounded border border-gray-800 bg-gray-900 p-3">전체 턴어라운드 수 {result.comparison.overall.turnaroundCount}</div>
        <div class="rounded border border-gray-800 bg-gray-900 p-3">필터 후 턴어라운드 수 {result.comparison.filtered.turnaroundCount}</div>
      </div>
    </div>

    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
      <div class="mb-3 text-sm font-semibold text-white">재무 스냅샷 업로드</div>
      <div class="mb-2 text-xs text-gray-400">JSON 배열 또는 단일 객체를 붙여넣으면 `symbol + asOfDate` 기준으로 upsert 됩니다.</div>
      <textarea bind:value={importPayload} class="h-56 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 font-mono text-xs text-white"></textarea>
      <div class="mt-3 flex justify-end">
        <button on:click={importSnapshots} disabled={importing} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700 disabled:opacity-40">
          {importing ? '업로드 중...' : '재무 데이터 업로드'}
        </button>
      </div>
    </div>
  </div>

  <div class="mt-4 overflow-auto rounded-xl border border-gray-800 bg-gray-950">
    <table class="min-w-full text-sm">
      <thead class="text-left text-gray-500">
        <tr>
          <th class="px-3 py-2">심볼</th>
          <th class="px-3 py-2">기준일</th>
          <th class="px-3 py-2">PER</th>
          <th class="px-3 py-2">PBR</th>
          <th class="px-3 py-2">ROE</th>
          <th class="px-3 py-2">매출 성장</th>
          <th class="px-3 py-2">순이익 성장</th>
          <th class="px-3 py-2">턴어라운드</th>
          <th class="px-3 py-2">시총</th>
        </tr>
      </thead>
      <tbody>
        {#if result.items.length === 0}
          <tr class="border-t border-gray-800">
            <td colspan="9" class="px-3 py-6 text-center text-sm text-gray-400">{loading ? '불러오는 중...' : '조건에 맞는 재무 팩터 종목이 없습니다.'}</td>
          </tr>
        {:else}
          {#each result.items as item}
            <tr class="border-t border-gray-800">
              <td class="px-3 py-2 font-medium text-white">{item.symbol}</td>
              <td class="px-3 py-2 text-gray-300">{item.asOfDate}</td>
              <td class="px-3 py-2 text-gray-300">{num(item.peRatio)}</td>
              <td class="px-3 py-2 text-gray-300">{num(item.pbRatio)}</td>
              <td class="px-3 py-2 text-gray-300">{item.roePercent != null ? `${num(item.roePercent)}%` : '-'}</td>
              <td class={`px-3 py-2 ${Number(item.revenueGrowthYoY ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{pct(item.revenueGrowthYoY, 1)}</td>
              <td class={`px-3 py-2 ${Number(item.netIncomeGrowthYoY ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{pct(item.netIncomeGrowthYoY, 1)}</td>
              <td class="px-3 py-2">{item.isTurnaround ? '예' : '-'}</td>
              <td class="px-3 py-2 text-gray-300">{marketCap(item.marketCap)}</td>
            </tr>
          {/each}
        {/if}
      </tbody>
    </table>
  </div>
</section>

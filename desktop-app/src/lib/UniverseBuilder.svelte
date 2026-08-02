<script>
  import { onMount } from 'svelte'
  import { universeApi } from '../api/endpoints'

  export let symbolsText = ''
  export let title = '시총 유니버스 빌더'
  export let description = '시가총액 백분위와 섹터/산업 필터로 후보 종목을 추려서 바로 백테스트/최적화 입력으로 넘깁니다.'
  export let candidateSymbols = []
  export let selectionSummary = null

  const presetOptions = [
    { id: 'small-cap-30', label: '소형주 하위 30%', percentileMin: '', percentileMax: '30', sortBy: 'marketCapAsc' },
    { id: 'micro-15', label: '초소형 하위 15%', percentileMin: '', percentileMax: '15', sortBy: 'marketCapAsc' },
    { id: 'mid-20-60', label: '중형 20~60%', percentileMin: '20', percentileMax: '60', sortBy: 'marketCapAsc' },
    { id: 'large-top-20', label: '대형 상위 20%', percentileMin: '80', percentileMax: '', sortBy: 'marketCapDesc' },
    { id: 'all', label: '전체', percentileMin: '', percentileMax: '', sortBy: 'marketCapAsc' }
  ]

  let loadingMeta = true
  let loadingItems = false
  let error = ''
  let meta = { totalActive: 0, marketCapCoverage: 0, sectors: [], industries: [] }
  let result = { totalUniverse: 0, matched: 0, items: [] }
  let filters = {
    preset: 'small-cap-30',
    search: '',
    selectedSectors: [],
    selectedIndustries: [],
    percentileMin: '',
    percentileMax: '30',
    minMarketCap: '',
    maxMarketCap: '',
    limit: 20,
    sortBy: 'marketCapAsc'
  }

  onMount(async () => {
    await loadMeta()
    await queryUniverse()
  })

  async function loadMeta() {
    loadingMeta = true
    try {
      const response = await universeApi.meta()
      meta = response.data ?? meta
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '유니버스 메타 정보를 불러오지 못했습니다.'
    } finally {
      loadingMeta = false
    }
  }

  async function queryUniverse() {
    loadingItems = true
    try {
      const response = await universeApi.query({
        search: filters.search || undefined,
        sectors: filters.selectedSectors.length ? filters.selectedSectors.join(',') : undefined,
        industries: filters.selectedIndustries.length ? filters.selectedIndustries.join(',') : undefined,
        percentileMin: filters.percentileMin || undefined,
        percentileMax: filters.percentileMax || undefined,
        marketCapMin: filters.minMarketCap || undefined,
        marketCapMax: filters.maxMarketCap || undefined,
        limit: filters.limit,
        sortBy: filters.sortBy
      })
      result = response.data ?? result
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '유니버스 후보를 불러오지 못했습니다.'
    } finally {
      loadingItems = false
    }
  }

  function applyPreset(presetId) {
    const preset = presetOptions.find((item) => item.id === presetId)
    if (!preset) return
    filters = {
      ...filters,
      preset: preset.id,
      percentileMin: preset.percentileMin,
      percentileMax: preset.percentileMax,
      sortBy: preset.sortBy
    }
    queryUniverse()
  }

  function toggleValue(key, value) {
    const current = filters[key]
    filters = {
      ...filters,
      [key]: current.includes(value)
        ? current.filter((item) => item !== value)
        : [...current, value]
    }
  }

  function replaceSymbols() {
    symbolsText = result.items.map((item) => item.symbol).join(', ')
  }

  function formatMarketCap(value) {
    const numeric = Number(value ?? 0)
    if (numeric >= 1_000_000_000_000) return `${(numeric / 1_000_000_000_000).toFixed(2)}T`
    if (numeric >= 1_000_000_000) return `${(numeric / 1_000_000_000).toFixed(2)}B`
    if (numeric >= 1_000_000) return `${(numeric / 1_000_000).toFixed(2)}M`
    return numeric.toLocaleString('en-US', { maximumFractionDigits: 0 })
  }

  function formatPercentile(value) {
    return `${Number(value ?? 0).toFixed(1)}%`
  }

  $: candidateSymbols = result.items.map((item) => item.symbol)
  $: selectionSummary = {
    totalUniverse: result.totalUniverse,
    matched: result.matched,
    previewCount: result.items.length,
    percentileMin: filters.percentileMin,
    percentileMax: filters.percentileMax,
    selectedSectorCount: filters.selectedSectors.length,
    selectedIndustryCount: filters.selectedIndustries.length
  }
</script>

<section class="rounded-2xl border border-violet-800/50 bg-violet-950/20 p-5">
  <div class="flex items-start justify-between gap-4">
    <div>
      <h3 class="text-base font-semibold text-violet-100">{title}</h3>
      <p class="mt-1 text-sm text-violet-50">{description}</p>
      <p class="mt-2 text-xs text-violet-200">이 영역은 시총/섹터/산업 유니버스 전용입니다. PER·턴어라운드·재무 변화는 위의 재무 팩터 빌더에서 다룹니다.</p>
    </div>
    <button on:click={replaceSymbols} disabled={result.items.length === 0} class="rounded bg-violet-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-violet-700 disabled:opacity-40">
      종목 입력에 반영
    </button>
  </div>

  {#if error}
    <div class="mt-4 rounded-lg border border-red-700 bg-red-900/20 p-4 text-sm text-red-300">{error}</div>
  {/if}

  <div class="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-4">
    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
      <div class="text-xs text-gray-500">활성 종목</div>
      <div class="mt-2 text-2xl font-bold text-white">{meta.totalActive}</div>
    </div>
    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
      <div class="text-xs text-gray-500">시총 커버리지</div>
      <div class="mt-2 text-2xl font-bold text-white">{meta.marketCapCoverage}</div>
    </div>
    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
      <div class="text-xs text-gray-500">매칭 종목</div>
      <div class="mt-2 text-2xl font-bold text-white">{result.matched}</div>
    </div>
    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
      <div class="text-xs text-gray-500">현재 반영 후보</div>
      <div class="mt-2 text-2xl font-bold text-white">{result.items.length}</div>
    </div>
  </div>

  <div class="mt-4 flex flex-wrap gap-2">
    {#each presetOptions as preset}
      <button on:click={() => applyPreset(preset.id)} class={`rounded-full px-3 py-2 text-sm transition ${filters.preset === preset.id ? 'bg-violet-600 text-white' : 'bg-gray-900 text-gray-300 hover:bg-gray-800'}`}>
        {preset.label}
      </button>
    {/each}
  </div>

  <div class="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-4">
    <label class="text-sm text-gray-300 xl:col-span-2">
      <div class="mb-2 text-gray-500">검색</div>
      <input bind:value={filters.search} on:change={queryUniverse} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="심볼, 이름, 섹터, 산업" />
    </label>
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">시총 백분위 최소</div>
      <input bind:value={filters.percentileMin} on:change={queryUniverse} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="예: 20" />
    </label>
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">시총 백분위 최대</div>
      <input bind:value={filters.percentileMax} on:change={queryUniverse} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="예: 30" />
    </label>
  </div>

  <div class="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-4">
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">시총 최소</div>
      <input bind:value={filters.minMarketCap} on:change={queryUniverse} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="예: 1000000000" />
    </label>
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">시총 최대</div>
      <input bind:value={filters.maxMarketCap} on:change={queryUniverse} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="예: 10000000000" />
    </label>
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">정렬</div>
      <select bind:value={filters.sortBy} on:change={queryUniverse} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
        <option value="marketCapAsc">시총 작은 순</option>
        <option value="marketCapDesc">시총 큰 순</option>
        <option value="symbol">심볼 순</option>
      </select>
    </label>
    <label class="text-sm text-gray-300">
      <div class="mb-2 text-gray-500">미리보기 수</div>
      <input type="number" min="1" max="100" bind:value={filters.limit} on:change={queryUniverse} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
    </label>
  </div>

  <div class="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-2">
    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
      <div class="mb-3 text-sm font-semibold text-white">섹터</div>
      {#if loadingMeta}
        <div class="text-sm text-gray-400">불러오는 중...</div>
      {:else}
        <div class="grid grid-cols-2 gap-2">
          {#each meta.sectors as sector}
            <label class="flex items-center gap-2 rounded border border-gray-800 bg-gray-900 px-3 py-2 text-xs text-gray-300">
              <input type="checkbox" checked={filters.selectedSectors.includes(sector.name)} on:change={() => { toggleValue('selectedSectors', sector.name); queryUniverse(); }} />
              <span class="truncate">{sector.name}</span>
              <span class="ml-auto text-gray-500">{sector.count}</span>
            </label>
          {/each}
        </div>
      {/if}
    </div>

    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
      <div class="mb-3 text-sm font-semibold text-white">산업</div>
      {#if loadingMeta}
        <div class="text-sm text-gray-400">불러오는 중...</div>
      {:else}
        <div class="grid grid-cols-2 gap-2">
          {#each meta.industries.slice(0, 12) as industry}
            <label class="flex items-center gap-2 rounded border border-gray-800 bg-gray-900 px-3 py-2 text-xs text-gray-300">
              <input type="checkbox" checked={filters.selectedIndustries.includes(industry.name)} on:change={() => { toggleValue('selectedIndustries', industry.name); queryUniverse(); }} />
              <span class="truncate">{industry.name}</span>
              <span class="ml-auto text-gray-500">{industry.count}</span>
            </label>
          {/each}
        </div>
      {/if}
    </div>
  </div>

  <div class="mt-4 flex justify-end">
    <button on:click={queryUniverse} disabled={loadingItems} class="rounded bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-40">
      {loadingItems ? '조회 중...' : '유니버스 다시 조회'}
    </button>
  </div>

  <div class="mt-4 overflow-auto rounded-xl border border-gray-800 bg-gray-950">
    <table class="min-w-full text-sm">
      <thead class="text-left text-gray-500">
        <tr>
          <th class="px-3 py-2">심볼</th>
          <th class="px-3 py-2">이름</th>
          <th class="px-3 py-2">섹터</th>
          <th class="px-3 py-2">산업</th>
          <th class="px-3 py-2">시총</th>
          <th class="px-3 py-2">백분위</th>
        </tr>
      </thead>
      <tbody>
        {#if result.items.length === 0}
          <tr class="border-t border-gray-800">
            <td colspan="6" class="px-3 py-6 text-center text-sm text-gray-400">조건에 맞는 종목이 없습니다.</td>
          </tr>
        {:else}
          {#each result.items as item}
            <tr class="border-t border-gray-800">
              <td class="px-3 py-2 font-medium text-white">{item.symbol}</td>
              <td class="px-3 py-2 text-gray-300">{item.name || '-'}</td>
              <td class="px-3 py-2 text-gray-300">{item.sector || '-'}</td>
              <td class="px-3 py-2 text-gray-300">{item.industry || '-'}</td>
              <td class="px-3 py-2 text-gray-300">{formatMarketCap(item.marketCap)}</td>
              <td class="px-3 py-2 text-gray-300">{formatPercentile(item.marketCapPercentile)}</td>
            </tr>
          {/each}
        {/if}
      </tbody>
    </table>
  </div>
</section>

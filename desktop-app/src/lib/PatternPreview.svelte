<script>
  import { Activity, AlertTriangle, RefreshCw, Search } from 'lucide-svelte'
  import { patternApi } from '../api/endpoints'

  export let pattern = null
  export let selectedRuleSummary = ''

  let symbol = 'TQQQ'
  let bars = 120
  let loading = false
  let error = ''
  let result = null
  let refreshTimer
  let lastPatternKey = ''
  let requestVersion = 0

  const width = 960
  const height = 360
  const left = 54
  const right = 18
  const top = 20
  const bottom = 38

  $: patternKey = pattern ? JSON.stringify(pattern) : ''
  $: if (patternKey && patternKey !== lastPatternKey) {
    lastPatternKey = patternKey
    scheduleRefresh()
  }

  $: chart = buildChart(result?.bars ?? [], result?.markers ?? [])

  function scheduleRefresh(delay = 450) {
    clearTimeout(refreshTimer)
    refreshTimer = setTimeout(loadPreview, delay)
  }

  async function loadPreview() {
    if (!pattern || !symbol.trim()) return
    const version = ++requestVersion
    loading = true
    error = ''
    try {
      const response = await patternApi.preview(symbol.trim().toUpperCase(), pattern, bars)
      if (version !== requestVersion) return
      result = response.data
      symbol = response.data.symbol
    } catch (e) {
      if (version !== requestVersion) return
      error = e?.response?.data?.error || e?.message || '차트 미리보기를 불러오지 못했습니다.'
    } finally {
      if (version === requestVersion) loading = false
    }
  }

  function submitSymbol(event) {
    event.preventDefault()
    loadPreview()
  }

  function setBars(value) {
    bars = value
    loadPreview()
  }

  function buildChart(sourceBars, markers) {
    if (!sourceBars.length) return null
    const plotWidth = width - left - right
    const plotHeight = height - top - bottom
    const low = Math.min(...sourceBars.map((bar) => Number(bar.low)))
    const high = Math.max(...sourceBars.map((bar) => Number(bar.high)))
    const padding = Math.max((high - low) * 0.08, high * 0.005)
    const minPrice = low - padding
    const maxPrice = high + padding
    const range = Math.max(maxPrice - minPrice, 0.0001)
    const slot = plotWidth / sourceBars.length
    const bodyWidth = Math.max(1.5, Math.min(7, slot * 0.64))
    const xForIndex = (index) => left + slot * index + slot / 2
    const yForPrice = (price) => top + (maxPrice - Number(price)) / range * plotHeight
    const byDate = new Map(sourceBars.map((bar, index) => [bar.date, index]))
    const candles = sourceBars.map((bar, index) => ({
      ...bar,
      x: xForIndex(index),
      openY: yForPrice(bar.open),
      highY: yForPrice(bar.high),
      lowY: yForPrice(bar.low),
      closeY: yForPrice(bar.close),
      bodyWidth,
      bullish: Number(bar.close) >= Number(bar.open)
    }))
    const markerPoints = markers
      .filter((marker) => byDate.has(marker.date))
      .map((marker) => ({ ...marker, x: xForIndex(byDate.get(marker.date)), y: yForPrice(marker.price) }))
    const ticks = Array.from({ length: 5 }, (_, index) => {
      const ratio = index / 4
      const price = maxPrice - range * ratio
      return { y: top + plotHeight * ratio, label: formatPrice(price) }
    })
    const dateTicks = [0, Math.floor((sourceBars.length - 1) / 2), sourceBars.length - 1]
      .map((index) => ({ x: xForIndex(index), label: sourceBars[index]?.date?.slice(5) }))
    const entryMarkers = markerPoints.filter((marker) => marker.type === 'ENTRY')
    return { candles, markerPoints, ticks, dateTicks, entryMarkers, yForPrice, plotWidth, plotHeight }
  }

  function formatPrice(value) {
    const number = Number(value)
    if (!Number.isFinite(number)) return '-'
    return number >= 1000 ? number.toLocaleString(undefined, { maximumFractionDigits: 0 }) : number.toFixed(2)
  }
</script>

<div class="mb-6 overflow-hidden rounded-xl border border-blue-900/70 bg-gray-950 shadow-lg shadow-blue-950/10">
  <div class="flex flex-wrap items-center justify-between gap-3 border-b border-gray-800 px-5 py-4">
    <div>
      <div class="flex items-center gap-2 text-sm font-semibold text-white">
        <Activity size={17} class="text-cyan-400" />
        실시간 타점 미리보기
        {#if loading}<RefreshCw size={14} class="animate-spin text-blue-400" />{/if}
      </div>
      <p class="mt-1 text-xs text-gray-400">규칙이나 수치를 바꾸면 현재 편집값으로 진입·청산 타점을 다시 계산합니다.</p>
    </div>
    <div class="flex flex-wrap items-center gap-2">
      <form on:submit={submitSymbol} class="flex overflow-hidden rounded border border-gray-700 bg-gray-900">
        <div class="flex items-center pl-3 text-gray-500"><Search size={14} /></div>
        <input bind:value={symbol} aria-label="미리보기 종목" class="w-24 bg-transparent px-2 py-2 text-sm font-semibold uppercase text-white outline-none" />
        <button type="submit" class="border-l border-gray-700 px-3 text-xs text-blue-300 hover:bg-gray-800">적용</button>
      </form>
      <div class="flex rounded border border-gray-700 bg-gray-900 p-1 text-xs">
        {#each [60, 120, 240] as count}
          <button on:click={() => setBars(count)} class={`rounded px-2 py-1 ${bars === count ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white'}`}>{count}봉</button>
        {/each}
      </div>
    </div>
  </div>

  {#if selectedRuleSummary}
    <div class="flex items-center gap-2 border-b border-gray-800 bg-blue-950/20 px-5 py-2 text-xs text-blue-100">
      <span class="rounded bg-blue-500/20 px-2 py-1 font-semibold text-blue-300">현재 선택</span>
      <span>{selectedRuleSummary}</span>
      <span class="text-gray-500">· 아래 타점은 선택 규칙을 포함한 전체 진입 조건 결과입니다.</span>
    </div>
  {/if}

  {#if error}
    <div class="m-5 flex items-start gap-3 rounded-lg border border-red-800 bg-red-950/20 p-4 text-sm text-red-200">
      <AlertTriangle size={18} class="mt-0.5 shrink-0" />
      <div>{error}</div>
    </div>
  {:else if chart}
    <div class="px-4 pb-3 pt-4">
      <div class="mb-3 flex flex-wrap items-center justify-between gap-2 px-1 text-xs">
        <div class="flex gap-4 text-gray-400">
          <span><span class="mr-1 inline-block h-2 w-2 rounded-full bg-emerald-400"></span>진입 {result.summary.entryCount}회</span>
          <span><span class="mr-1 inline-block h-2 w-2 rounded-full bg-rose-400"></span>청산 {result.summary.exitCount}회</span>
          <span>{result.summary.from} ~ {result.summary.to}</span>
        </div>
        <div class={result.summary.openPosition ? 'text-amber-300' : 'text-gray-500'}>
          {result.summary.openPosition ? '마지막 포지션 보유 중' : '마지막 포지션 없음'}
        </div>
      </div>

      <div class="overflow-x-auto rounded-lg border border-gray-800 bg-[#080d18]">
        <svg viewBox={`0 0 ${width} ${height}`} class="block min-w-[720px] w-full" role="img" aria-label={`${result.symbol} 패턴 타점 차트`}>
          {#each chart.ticks as tick}
            <line x1={left} x2={width - right} y1={tick.y} y2={tick.y} stroke="#1f2937" stroke-width="1" />
            <text x={left - 7} y={tick.y + 4} text-anchor="end" fill="#6b7280" font-size="10">{tick.label}</text>
          {/each}

          {#each chart.entryMarkers as marker}
            {#if marker.stopPrice != null}
              <line x1={marker.x} x2={width - right} y1={chart.yForPrice(marker.stopPrice)} y2={chart.yForPrice(marker.stopPrice)} stroke="#f87171" stroke-width="1" stroke-dasharray="4 5" opacity="0.32" />
            {/if}
            {#if marker.targetPrice != null}
              <line x1={marker.x} x2={width - right} y1={chart.yForPrice(marker.targetPrice)} y2={chart.yForPrice(marker.targetPrice)} stroke="#34d399" stroke-width="1" stroke-dasharray="4 5" opacity="0.28" />
            {/if}
          {/each}

          {#each chart.candles as candle}
            <line x1={candle.x} x2={candle.x} y1={candle.highY} y2={candle.lowY} stroke={candle.bullish ? '#34d399' : '#fb7185'} stroke-width="1" />
            <rect
              x={candle.x - candle.bodyWidth / 2}
              y={Math.min(candle.openY, candle.closeY)}
              width={candle.bodyWidth}
              height={Math.max(1, Math.abs(candle.closeY - candle.openY))}
              fill={candle.bullish ? '#10b981' : '#f43f5e'}
              opacity="0.9"
            />
          {/each}

          {#each chart.markerPoints as marker}
            {#if marker.type === 'ENTRY'}
              <path d={`M ${marker.x} ${marker.y + 13} L ${marker.x - 6} ${marker.y + 22} L ${marker.x + 6} ${marker.y + 22} Z`} fill="#34d399">
                <title>진입 {marker.date} · {formatPrice(marker.price)}&#10;손절 {formatPrice(marker.stopPrice)} · 목표 {formatPrice(marker.targetPrice)}&#10;{marker.details ?? ''}</title>
              </path>
            {:else}
              <path d={`M ${marker.x} ${marker.y - 13} L ${marker.x - 6} ${marker.y - 22} L ${marker.x + 6} ${marker.y - 22} Z`} fill="#fb7185">
                <title>청산 {marker.date} · {formatPrice(marker.price)}&#10;{marker.reason ?? ''}</title>
              </path>
            {/if}
          {/each}

          {#each chart.dateTicks as tick}
            <text x={tick.x} y={height - 12} text-anchor="middle" fill="#6b7280" font-size="10">{tick.label}</text>
          {/each}
        </svg>
      </div>

      <div class="mt-3 flex flex-wrap gap-x-5 gap-y-1 px-1 text-xs text-gray-500">
        <span>▲ 초록: 진입</span>
        <span>▼ 빨강: 청산</span>
        <span>점선: 각 진입 시점의 손절가·목표가</span>
        <span>마커에 마우스를 올리면 판단 근거를 확인할 수 있습니다.</span>
      </div>

      {#if result.warnings?.length}
        <div class="mt-3 space-y-1 rounded border border-amber-900/60 bg-amber-950/20 px-3 py-2 text-xs text-amber-200">
          {#each result.warnings as warning}<div>• {warning}</div>{/each}
        </div>
      {/if}
    </div>
  {:else}
    <div class="flex h-64 items-center justify-center text-sm text-gray-500">
      {loading ? '현재 규칙으로 타점을 계산하고 있습니다...' : '패턴을 선택하면 차트 미리보기가 표시됩니다.'}
    </div>
  {/if}
</div>

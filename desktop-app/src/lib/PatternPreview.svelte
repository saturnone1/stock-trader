<script>
  import { onDestroy } from 'svelte'
  import { Activity, AlertTriangle, CalendarDays, RefreshCw, Search, TrendingUp } from 'lucide-svelte'
  import { patternApi } from '../api/endpoints'

  export let pattern = null
  export let selectedRuleSummary = ''

  const timeFrames = [
    { value: 'OneMinute', label: '1분봉' },
    { value: 'FiveMinute', label: '5분봉' },
    { value: 'FifteenMinute', label: '15분봉' },
    { value: 'Daily', label: '일봉' },
    { value: 'Weekly', label: '주봉' }
  ]

  let symbol = 'TQQQ'
  let timeFrame = 'Daily'
  let toDate = isoDate(new Date())
  let fromDate = defaultFromDate(timeFrame, toDate)
  let loading = false
  let error = ''
  let result = null
  let comparison = null
  let refreshTimer
  let lastPatternKey = ''
  let lastSuccessfulContext = ''
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
    scheduleRefresh('pattern')
  }
  $: chart = buildChart(result?.bars ?? [], result?.markers ?? [], result?.matches ?? [])
  $: presets = presetsFor(timeFrame)

  onDestroy(() => clearTimeout(refreshTimer))

  function scheduleRefresh(reason = 'pattern', delay = 500) {
    clearTimeout(refreshTimer)
    refreshTimer = setTimeout(() => loadPreview(reason), delay)
  }

  async function loadPreview(reason = 'manual') {
    if (!pattern || !symbol.trim() || !fromDate || !toDate) return
    if (fromDate > toDate) {
      error = '조회 시작일은 종료일보다 앞서야 합니다.'
      return
    }

    const version = ++requestVersion
    const strategyId = pattern.id ?? pattern.name ?? 'strategy'
    const context = `${symbol.trim().toUpperCase()}|${timeFrame}|${fromDate}|${toDate}|${strategyId}`
    const previous = result
    loading = true
    error = ''

    try {
      const response = await patternApi.preview(symbol.trim().toUpperCase(), pattern, {
        bars: 600,
        timeFrame,
        from: fromDate,
        to: toDate
      })
      if (version !== requestVersion) return

      result = response.data
      symbol = response.data.symbol
      if (reason === 'pattern' && previous?.summary && lastSuccessfulContext === context) {
        comparison = {
          entryDelta: result.summary.entryCount - previous.summary.entryCount,
          matchDelta: result.summary.matchCount - previous.summary.matchCount,
          previousEntries: previous.summary.entryCount,
          currentEntries: result.summary.entryCount
        }
      } else {
        comparison = null
      }
      lastSuccessfulContext = context
    } catch (e) {
      if (version !== requestVersion) return
      error = e?.response?.data?.error || e?.message || '차트 미리보기를 불러오지 못했습니다.'
    } finally {
      if (version === requestVersion) loading = false
    }
  }

  function submitFilters(event) {
    event?.preventDefault()
    loadPreview('filters')
  }

  function changeTimeFrame(value) {
    timeFrame = value
    fromDate = defaultFromDate(value, toDate)
    comparison = null
    loadPreview('filters')
  }

  function applyPreset(days) {
    fromDate = shiftDate(toDate, -(days - 1))
    comparison = null
    loadPreview('filters')
  }

  function presetsFor(value) {
    if (value === 'OneMinute') return [{ label: '1일', days: 1 }, { label: '3일', days: 3 }, { label: '7일', days: 7 }]
    if (value === 'FiveMinute') return [{ label: '5일', days: 5 }, { label: '2주', days: 14 }, { label: '1개월', days: 30 }]
    if (value === 'FifteenMinute') return [{ label: '1개월', days: 30 }, { label: '3개월', days: 90 }, { label: '4개월', days: 120 }]
    if (value === 'Weekly') return [{ label: '1년', days: 365 }, { label: '3년', days: 1095 }, { label: '5년', days: 1825 }]
    return [{ label: '3개월', days: 90 }, { label: '6개월', days: 180 }, { label: '1년', days: 365 }, { label: '3년', days: 1095 }]
  }

  function defaultFromDate(value, endDate) {
    const preset = value === 'OneMinute' ? 1 : value === 'FiveMinute' ? 5 : value === 'FifteenMinute' ? 20 : value === 'Weekly' ? 1095 : 365
    return shiftDate(endDate, -(preset - 1))
  }

  function shiftDate(value, days) {
    const date = new Date(`${value}T00:00:00Z`)
    date.setUTCDate(date.getUTCDate() + days)
    return isoDate(date)
  }

  function isoDate(date) {
    return date.toISOString().slice(0, 10)
  }

  function buildChart(sourceBars, markers, matches) {
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
    const bodyWidth = Math.max(1.2, Math.min(7, slot * 0.64))
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
    const matchPoints = matches
      .filter((match) => byDate.has(match.date))
      .map((match) => ({ ...match, x: xForIndex(byDate.get(match.date)), y: yForPrice(match.price) }))
    const ticks = Array.from({ length: 5 }, (_, index) => {
      const ratio = index / 4
      const price = maxPrice - range * ratio
      return { y: top + plotHeight * ratio, label: formatPrice(price) }
    })
    const dateTicks = [0, Math.floor((sourceBars.length - 1) / 2), sourceBars.length - 1]
      .map((index) => ({ x: xForIndex(index), label: formatAxisDate(sourceBars[index]?.date) }))
    const entryMarkers = markerPoints.filter((marker) => marker.type === 'ENTRY')
    return { candles, markerPoints, matchPoints, ticks, dateTicks, entryMarkers, yForPrice }
  }

  function formatPrice(value) {
    const number = Number(value)
    if (!Number.isFinite(number)) return '-'
    return number >= 1000 ? number.toLocaleString(undefined, { maximumFractionDigits: 0 }) : number.toFixed(2)
  }

  function formatAxisDate(value) {
    if (!value) return ''
    const date = value.slice(5, 10)
    return ['OneMinute', 'FiveMinute', 'FifteenMinute'].includes(timeFrame)
      ? `${date} ${value.slice(11, 16)}`
      : date
  }

  function deltaLabel(value, noun) {
    if (value > 0) return `${noun} ${value}회 증가`
    if (value < 0) return `${noun} ${Math.abs(value)}회 감소`
    return `${noun} 변화 없음`
  }
</script>

<div class="mb-6 overflow-hidden rounded-xl border border-blue-900/70 bg-gray-950 shadow-lg shadow-blue-950/10">
  <div class="border-b border-gray-800 px-5 py-4">
    <div class="mb-4 flex flex-wrap items-start justify-between gap-3">
      <div>
        <div class="flex items-center gap-2 text-base font-semibold text-white">
          <Activity size={18} class="text-cyan-400" />
          차트로 매수·매도 시점 확인
          {#if loading}<RefreshCw size={14} class="animate-spin text-blue-400" />{/if}
        </div>
        <p class="mt-1 text-xs text-gray-400">종목과 기간을 고른 뒤 조건을 바꾸면, 매수 후보와 실제 거래 시점이 자동으로 다시 계산됩니다.</p>
      </div>
      <div class="text-xs text-gray-500">표시 한도 600봉 · 날짜는 미국 거래일 기준</div>
    </div>

    <form on:submit={submitFilters} class="space-y-3">
      <div class="grid gap-3 lg:grid-cols-[9rem,1fr,10rem,10rem,auto]">
        <label class="block text-xs text-gray-400">
          <span class="mb-1 block">종목</span>
          <div class="flex overflow-hidden rounded border border-gray-700 bg-gray-900">
            <span class="flex items-center pl-3 text-gray-500"><Search size={14} /></span>
            <input bind:value={symbol} aria-label="차트 종목" class="min-w-0 flex-1 bg-transparent px-2 py-2 text-sm font-semibold uppercase text-white outline-none" />
          </div>
        </label>

        <fieldset class="text-xs text-gray-400">
          <legend class="mb-1">봉 단위</legend>
          <div class="flex h-[38px] rounded border border-gray-700 bg-gray-900 p-1">
            {#each timeFrames as option}
              <button type="button" on:click={() => changeTimeFrame(option.value)} class={`flex-1 rounded px-2 text-xs ${timeFrame === option.value ? 'bg-blue-600 font-semibold text-white' : 'text-gray-400 hover:text-white'}`}>{option.label}</button>
            {/each}
          </div>
        </fieldset>

        <label class="block text-xs text-gray-400">
          <span class="mb-1 flex items-center gap-1"><CalendarDays size={12} /> 시작일</span>
          <input type="date" bind:value={fromDate} max={toDate} class="w-full rounded border border-gray-700 bg-gray-900 px-2 py-2 text-sm text-white" />
        </label>

        <label class="block text-xs text-gray-400">
          <span class="mb-1 flex items-center gap-1"><CalendarDays size={12} /> 종료일</span>
          <input type="date" bind:value={toDate} min={fromDate} class="w-full rounded border border-gray-700 bg-gray-900 px-2 py-2 text-sm text-white" />
        </label>

        <button type="submit" class="mt-5 h-[38px] rounded bg-blue-600 px-4 text-sm font-semibold text-white hover:bg-blue-700">차트 보기</button>
      </div>

      <div class="flex flex-wrap items-center gap-2 text-xs">
        <span class="text-gray-500">빠른 기간</span>
        {#each presets as preset}
          <button type="button" on:click={() => applyPreset(preset.days)} class="rounded border border-gray-700 bg-gray-900 px-2 py-1 text-gray-300 hover:border-blue-600 hover:text-white">{preset.label}</button>
        {/each}
      </div>
    </form>
  </div>

  {#if selectedRuleSummary}
    <div class="flex flex-wrap items-center gap-2 border-b border-gray-800 bg-blue-950/20 px-5 py-2 text-xs text-blue-100">
      <span class="rounded bg-blue-500/20 px-2 py-1 font-semibold text-blue-300">지금 바꾸는 조건</span>
      <span>{selectedRuleSummary}</span>
      <span class="text-gray-500">· 차트는 이 조건을 포함한 전체 매수 전략의 결과입니다.</span>
    </div>
  {/if}

  {#if comparison}
    <div class="flex flex-wrap items-center gap-3 border-b border-emerald-900/50 bg-emerald-950/20 px-5 py-3 text-xs">
      <span class="flex items-center gap-1 font-semibold text-emerald-300"><TrendingUp size={14} /> 방금 바꾼 설정의 영향</span>
      <span class={comparison.entryDelta > 0 ? 'text-emerald-200' : comparison.entryDelta < 0 ? 'text-amber-200' : 'text-gray-300'}>{deltaLabel(comparison.entryDelta, '실제 매수')}</span>
      <span class="text-gray-300">{deltaLabel(comparison.matchDelta, '매수 조건 충족')}</span>
      <span class="text-gray-500">이전 {comparison.previousEntries}회 → 현재 {comparison.currentEntries}회</span>
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
        <div class="flex flex-wrap gap-4 text-gray-400">
          <span><span class="mr-1 inline-block h-2 w-2 rounded-full bg-amber-400"></span>매수 조건 충족 {result.summary.matchCount}회</span>
          <span><span class="mr-1 inline-block h-2 w-2 rounded-full bg-emerald-400"></span>실제 매수 {result.summary.entryCount}회</span>
          <span><span class="mr-1 inline-block h-2 w-2 rounded-full bg-rose-400"></span>매도 {result.summary.exitCount}회</span>
          <span class="text-cyan-300">추가 매수 {result.summary.scaleInCount ?? 0}회</span>
          <span class="text-orange-300">일부 매도 {result.summary.partialExitCount ?? 0}회</span>
          <span>{result.summary.requestedFrom} ~ {result.summary.requestedTo}</span>
        </div>
        <div class={result.summary.openPosition ? 'text-amber-300' : 'text-gray-500'}>
          {result.summary.openPosition ? '마지막 포지션 보유 중' : '마지막 포지션 없음'}
        </div>
      </div>

      <div class="overflow-x-auto rounded-lg border border-gray-800 bg-[#080d18]">
        <svg viewBox={`0 0 ${width} ${height}`} class="block min-w-[720px] w-full" role="img" aria-label={`${result.symbol} 매매 시점 차트`}>
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
            <rect x={candle.x - candle.bodyWidth / 2} y={Math.min(candle.openY, candle.closeY)} width={candle.bodyWidth} height={Math.max(1, Math.abs(candle.closeY - candle.openY))} fill={candle.bullish ? '#10b981' : '#f43f5e'} opacity="0.9" />
          {/each}

          {#each chart.matchPoints as match}
            <circle cx={match.x} cy={match.y} r="3.2" fill="#fbbf24" opacity="0.72">
              <title>매수 조건 충족 · {formatAxisDate(match.date)} · {formatPrice(match.price)}&#10;{match.details ?? ''}</title>
            </circle>
          {/each}

          {#each chart.markerPoints as marker}
            {#if marker.type === 'ENTRY'}
              <path d={`M ${marker.x} ${marker.y + 13} L ${marker.x - 6} ${marker.y + 22} L ${marker.x + 6} ${marker.y + 22} Z`} fill="#34d399">
                <title>실제 매수 {formatAxisDate(marker.date)} · {formatPrice(marker.price)}&#10;손절 {formatPrice(marker.stopPrice)} · 목표 {formatPrice(marker.targetPrice)}&#10;{marker.details ?? ''}</title>
              </path>
            {:else if marker.type === 'EXIT'}
              <path d={`M ${marker.x} ${marker.y - 13} L ${marker.x - 6} ${marker.y - 22} L ${marker.x + 6} ${marker.y - 22} Z`} fill="#fb7185">
                <title>매도 {formatAxisDate(marker.date)} · {formatPrice(marker.price)}&#10;{marker.reason ?? ''}</title>
              </path>
            {:else if marker.type === 'SCALE_IN'}
              <circle cx={marker.x} cy={marker.y} r="6" fill="#22d3ee" stroke="#083344" stroke-width="2">
                <title>추가 매수 {formatAxisDate(marker.date)} · {formatPrice(marker.price)}&#10;{marker.details ?? ''}</title>
              </circle>
            {:else if marker.type === 'STOP_MOVE'}
              <rect x={marker.x - 4} y={marker.y - 4} width="8" height="8" transform={`rotate(45 ${marker.x} ${marker.y})`} fill="#a78bfa">
                <title>손절가 상향 {formatAxisDate(marker.date)} · {formatPrice(marker.price)}&#10;{marker.details ?? ''}</title>
              </rect>
            {:else}
              <rect x={marker.x - 5} y={marker.y - 5} width="10" height="10" fill="#fb923c">
                <title>일부 매도 {formatAxisDate(marker.date)} · {formatPrice(marker.price)}&#10;{marker.reason ?? marker.details ?? ''}</title>
              </rect>
            {/if}
          {/each}

          {#each chart.dateTicks as tick}
            <text x={tick.x} y={height - 12} text-anchor="middle" fill="#6b7280" font-size="10">{tick.label}</text>
          {/each}
        </svg>
      </div>

      <div class="mt-3 flex flex-wrap gap-x-5 gap-y-1 px-1 text-xs text-gray-500">
        <span>● 노랑: 매수 조건을 만족한 모든 봉</span>
        <span>▲ 초록: 포지션이 없어 실제 매수한 시점</span>
        <span>▼ 빨강: 매도 시점</span>
        <span>● 하늘: 추가 매수</span>
        <span>■ 주황: 부분 익절·일부 매도</span>
        <span>◆ 보라: 손절가 상향</span>
        <span>점선: 실제 매수 당시 손절가·목표가</span>
      </div>

      {#if result.warnings?.length}
        <div class="mt-3 space-y-1 rounded border border-amber-900/60 bg-amber-950/20 px-3 py-2 text-xs text-amber-200">
          {#each result.warnings as warning}<div>• {warning}</div>{/each}
        </div>
      {/if}
    </div>
  {:else}
    <div class="flex h-64 items-center justify-center text-sm text-gray-500">
      {loading ? '현재 조건으로 매수·매도 시점을 계산하고 있습니다...' : '전략을 선택하면 차트 미리보기가 표시됩니다.'}
    </div>
  {/if}
</div>

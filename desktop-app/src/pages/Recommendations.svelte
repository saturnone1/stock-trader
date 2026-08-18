<script>
  import { onMount } from 'svelte'
  import { analysisApi, orderApi, tradeApi } from '../api/endpoints'

  let loading = true
  let analysisLoading = false
  let error = ''
  let notice = ''
  let rows = []
  let selectedSymbol = ''
  let analysis = null
  let reconcilingId = null

  onMount(load)

  function pct(value, digits = 1) {
    return `${Number(value ?? 0).toFixed(digits)}%`
  }

  function gradeColor(grade) {
    if (grade === 'StrongBuy' || grade === 'Buy') return 'text-green-300'
    if (grade === 'StrongSell' || grade === 'Sell') return 'text-red-300'
    return 'text-yellow-300'
  }

  function entryStatus(row) {
    return row.EntryStatus ?? row.entryStatus ?? 'Ready'
  }

  function entryStatusLabel(status) {
    return ({
      Ready: '주문 전',
      SubmissionUnconfirmed: '접수 확인 필요',
      AwaitingBroker: '체결 대기',
      Completed: '체결 반영 완료',
      Failed: '주문 실패',
    })[status] ?? status
  }

  function entryStatusClass(status) {
    if (status === 'Completed') return 'text-green-300'
    if (status === 'SubmissionUnconfirmed' || status === 'Failed') return 'text-red-300'
    if (status === 'AwaitingBroker') return 'text-yellow-300'
    return 'text-gray-400'
  }

  async function reconcileEntry(row) {
    const id = row.Id ?? row.id
    reconcilingId = id
    try {
      const { data } = await orderApi.reconcileEntryOrder(id)
      await load()
      notice = data?.message ?? '진입 주문 상태를 확인했습니다.'
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '진입 주문 상태를 확인하지 못했습니다.'
    } finally {
      reconcilingId = null
    }
  }

  async function load() {
    loading = true
    try {
      const { data } = await tradeApi.recommendations()
      rows = data?.recommendations ?? data?.Recommendations ?? []
      error = ''

      if (rows.length > 0) {
        selectedSymbol = rows[0].Symbol ?? rows[0].symbol
        await loadAnalysis(selectedSymbol)
      } else {
        analysis = null
      }
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '추천 데이터를 불러오지 못했습니다.'
    } finally {
      loading = false
    }
  }

  async function loadAnalysis(symbol) {
    selectedSymbol = symbol
    analysisLoading = true
    try {
      const { data } = await analysisApi.get(symbol)
      analysis = data
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '상세 분석을 불러오지 못했습니다.'
    } finally {
      analysisLoading = false
    }
  }
</script>

<div class="flex h-full overflow-hidden">
  <aside class="w-96 shrink-0 overflow-y-auto border-r border-gray-800 bg-gray-950">
    <div class="border-b border-gray-800 p-6">
      <div class="mb-2 text-3xl font-bold">종목 추천</div>
      <div class="text-sm text-gray-400">추천 목록과 종목별 상세 분석</div>
      <button on:click={load} class="mt-4 rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">
        새로고침
      </button>
    </div>

    {#if loading}
      <div class="p-6 text-gray-400">불러오는 중...</div>
    {:else if rows.length === 0}
      <div class="p-6 text-gray-400">추천 결과가 없습니다.</div>
    {:else}
      <div class="space-y-2 p-4">
        {#each rows as row}
          {@const symbol = row.Symbol ?? row.symbol}
          {@const status = entryStatus(row)}
          <div
            class={`w-full rounded-lg border p-4 transition ${
              selectedSymbol === symbol
                ? 'border-blue-600 bg-blue-950/30'
                : 'border-gray-800 bg-gray-900 hover:border-gray-700'
            }`}
          >
            <button on:click={() => loadAnalysis(symbol)} class="w-full text-left">
            <div class="mb-2 flex items-start justify-between">
              <div>
                <div class="font-mono text-lg text-blue-400">{symbol}</div>
                <div class="text-xs text-gray-400">{row.Pattern ?? row.pattern}</div>
              </div>
              <div class="text-right text-xs text-gray-400">
                <div>{row.Mode ?? row.mode}</div>
                <div>R/R {(row.RiskRewardRatio ?? row.riskRewardRatio ?? 0).toFixed(2)}</div>
                <div class={entryStatusClass(status)}>{entryStatusLabel(status)}</div>
              </div>
            </div>
            <div class="grid grid-cols-3 gap-2 text-xs">
              <div><div class="text-gray-500">Entry</div><div>{(row.EntryPrice ?? row.entryPrice ?? 0).toFixed(2)}</div></div>
              <div><div class="text-gray-500">Stop</div><div class="text-red-300">{(row.StopLossPrice ?? row.stopLossPrice ?? 0).toFixed(2)}</div></div>
              <div><div class="text-gray-500">Target</div><div class="text-green-300">{(row.TargetPrice ?? row.targetPrice ?? 0).toFixed(2)}</div></div>
            </div>
            </button>
            {#if status === 'SubmissionUnconfirmed' || status === 'AwaitingBroker'}
              <div class="mt-3 border-t border-gray-800 pt-3">
                <div class="mb-2 text-xs text-yellow-200">
                  자동 재조정 중입니다. 확정 전에는 같은 주문을 다시 실행하지 마세요.
                </div>
                <button
                  on:click={() => reconcileEntry(row)}
                  disabled={reconcilingId === (row.Id ?? row.id)}
                  class="rounded bg-yellow-700 px-3 py-1.5 text-xs text-white hover:bg-yellow-600 disabled:opacity-50"
                >
                  {reconcilingId === (row.Id ?? row.id) ? '확인 중...' : '지금 브로커 상태 확인'}
                </button>
              </div>
            {/if}
            {#if row.Note ?? row.note}
              <div class="mt-2 text-xs text-red-300">{row.Note ?? row.note}</div>
            {/if}
          </div>
        {/each}
      </div>
    {/if}
  </aside>

  <main class="flex-1 overflow-auto p-8">
    {#if error}
      <div class="mb-6 rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
    {/if}
    {#if notice}
      <div class="mb-6 rounded-lg border border-green-700 bg-green-900/20 p-4 text-green-300">{notice}</div>
    {/if}

    {#if analysisLoading}
      <div class="text-gray-400">상세 분석 불러오는 중...</div>
    {:else if analysis}
      <div class="mb-8 flex items-start justify-between">
        <div>
          <h2 class="text-4xl font-bold">{analysis.Symbol}</h2>
          <div class="mt-2 text-sm text-gray-400">Analyzed at {analysis.AnalyzedAt}</div>
        </div>
        <div class={`text-2xl font-bold ${gradeColor(analysis.Grade)}`}>{analysis.Grade}</div>
      </div>

      <div class="mb-8 grid grid-cols-6 gap-4">
        <div class="rounded-lg border border-gray-700 bg-gray-800 p-4"><div class="text-sm text-gray-400">현재가</div><div class="mt-2 text-2xl font-bold">{Number(analysis.CurrentPrice ?? 0).toFixed(2)}</div></div>
        <div class="rounded-lg border border-gray-700 bg-gray-800 p-4"><div class="text-sm text-gray-400">상승 확률</div><div class="mt-2 text-2xl font-bold text-green-300">{pct(analysis.UpsideProbability)}</div></div>
        <div class="rounded-lg border border-gray-700 bg-gray-800 p-4"><div class="text-sm text-gray-400">예상 수익률</div><div class="mt-2 text-2xl font-bold {(analysis.ExpectedReturnPercent ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}">{pct(analysis.ExpectedReturnPercent, 2)}</div></div>
        <div class="rounded-lg border border-gray-700 bg-gray-800 p-4"><div class="text-sm text-gray-400">손실 위험</div><div class="mt-2 text-2xl font-bold text-red-300">{pct(analysis.DownsideRiskPercent, 1)}</div></div>
        <div class="rounded-lg border border-gray-700 bg-gray-800 p-4"><div class="text-sm text-gray-400">신뢰도</div><div class="mt-2 text-2xl font-bold">{Number(analysis.ConfidenceScore ?? 0).toFixed(0)}</div></div>
        <div class="rounded-lg border border-gray-700 bg-gray-800 p-4"><div class="text-sm text-gray-400">예상 기간</div><div class="mt-2 text-2xl font-bold">{analysis.ExpectedHoldingDays ?? 0}일</div></div>
      </div>

      <div class="mb-8 grid grid-cols-2 gap-6">
        <section class="rounded-lg border border-gray-700 bg-gray-800 p-6">
          <h3 class="mb-4 text-xl font-bold">추천 가격대</h3>
          <div class="grid grid-cols-3 gap-4 text-sm">
            <div><div class="text-gray-500">현재가</div><div class="mt-1 text-lg">{Number(analysis.CurrentPrice ?? 0).toFixed(2)}</div></div>
            <div><div class="text-gray-500">추천 손절선</div><div class="mt-1 text-lg text-red-300">{Number(analysis.RecommendedStopLoss ?? 0).toFixed(2)}</div></div>
            <div><div class="text-gray-500">추천 목표가</div><div class="mt-1 text-lg text-green-300">{Number(analysis.RecommendedTarget ?? 0).toFixed(2)}</div></div>
          </div>
          <div class="mt-4 text-sm text-gray-400">ATR {Number(analysis.ATR ?? 0).toFixed(2)}</div>
        </section>

        <section class="rounded-lg border border-gray-700 bg-gray-800 p-6">
          <h3 class="mb-4 text-xl font-bold">기술 지표</h3>
          <div class="grid grid-cols-2 gap-3 text-sm">
            <div class="flex justify-between"><span class="text-gray-400">RSI</span><span>{Number(analysis.Indicators?.RSI ?? 0).toFixed(2)}</span></div>
            <div class="flex justify-between"><span class="text-gray-400">MACD</span><span>{Number(analysis.Indicators?.MACD ?? 0).toFixed(3)}</span></div>
            <div class="flex justify-between"><span class="text-gray-400">SMA20</span><span>{Number(analysis.Indicators?.SMA20 ?? 0).toFixed(2)}</span></div>
            <div class="flex justify-between"><span class="text-gray-400">SMA50</span><span>{Number(analysis.Indicators?.SMA50 ?? 0).toFixed(2)}</span></div>
            <div class="flex justify-between"><span class="text-gray-400">SMA200</span><span>{Number(analysis.Indicators?.SMA200 ?? 0).toFixed(2)}</span></div>
            <div class="flex justify-between"><span class="text-gray-400">VWAP</span><span>{Number(analysis.Indicators?.VWAP ?? 0).toFixed(2)}</span></div>
            <div class="flex justify-between"><span class="text-gray-400">Bullish Count</span><span>{analysis.Indicators?.BullishIndicatorCount ?? 0}</span></div>
            <div class="flex justify-between"><span class="text-gray-400">Total Indicators</span><span>{analysis.Indicators?.TotalIndicatorCount ?? 0}</span></div>
          </div>
        </section>
      </div>

      <section class="rounded-lg border border-gray-700 bg-gray-800 p-6">
        <h3 class="mb-4 text-xl font-bold">활성 패턴</h3>
        {#if (analysis.ActivePatterns ?? []).length === 0}
          <div class="text-sm text-gray-400">감지된 패턴이 없습니다.</div>
        {:else}
          <div class="grid grid-cols-3 gap-4">
            {#each analysis.ActivePatterns as pattern}
              <div class="rounded border border-gray-700 bg-gray-900 p-4">
                <div class="font-semibold">{pattern.Pattern}</div>
                <div class="mt-3 space-y-1 text-sm">
                  <div class="flex justify-between"><span class="text-gray-400">Confidence</span><span>{pct(pattern.Confidence)}</span></div>
                  <div class="flex justify-between"><span class="text-gray-400">Hist. Win Rate</span><span>{pct(pattern.HistoricalWinRate)}</span></div>
                  <div class="flex justify-between"><span class="text-gray-400">Hist. Avg Return</span><span>{pct(pattern.HistoricalAvgReturn, 2)}</span></div>
                </div>
              </div>
            {/each}
          </div>
        {/if}
      </section>
    {:else}
      <div class="text-gray-400">추천 종목을 선택하세요.</div>
    {/if}
  </main>
</div>

<script>
  import { formatBacktestTimestamp, formatMoney, formatPercentagePoints, formatPercent, formatSignedPercent } from './backtestResearch'

  export let result
  export let timingReport = null
</script>

{#if result.errorMessage}
  <div class="rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">
    <strong>백테스트 실패:</strong> {result.errorMessage}
  </div>
{/if}

{#if result.survivorshipBiasWarning}
  <div class="rounded-2xl border border-orange-700 bg-orange-950/20 p-4 text-sm text-orange-200">
    <div class="font-semibold">종목 선택 편향 주의</div>
    <div class="mt-1">{result.survivorshipBiasWarning}</div>
  </div>
{/if}

{#if result.warnings?.length}
  <div class="rounded-2xl border border-yellow-700 bg-yellow-900/10 p-5">
    <div class="mb-3 text-sm font-semibold text-yellow-300">경고 {result.warnings.length}건</div>
    <div class="space-y-2">
      {#each result.warnings as warning}
        <div class="rounded border border-yellow-700/60 bg-yellow-950/30 p-3 text-sm text-yellow-200">{warning}</div>
      {/each}
    </div>
  </div>
{/if}

<div class="flex flex-wrap items-center gap-3 text-sm text-gray-400">
  <span class="rounded bg-blue-950/40 px-3 py-1 text-blue-300">{result.usedTimeFrame} 백테스트</span>
  {#if result.request?.universeVariant}
    <span class="rounded bg-violet-950/40 px-3 py-1 text-violet-300">{result.request.universeVariant.label} · {result.request.universeVariant.symbolCount}개</span>
  {/if}
  {#if result.timingScenario}
    <span class="rounded bg-emerald-950/40 px-3 py-1 text-emerald-300">{result.timingScenario.label}</span>
  {/if}
  <span>종목: {result.request.symbols.join(', ')}</span>
  <span>패턴: {result.request.patternNames.join(', ')}</span>
  {#if result.actualDataFrom}
    <span>실제 데이터 시작: {formatBacktestTimestamp(result.actualDataFrom, result.usedTimeFrame)}</span>
  {/if}
</div>

<div class="grid grid-cols-2 gap-4 xl:grid-cols-6">
  <div class="rounded-xl border border-gray-800 bg-gray-950 p-4 text-center">
    <div class="text-xs text-gray-500">총 수익률</div>
    <div class={`mt-2 text-2xl font-bold ${Number(result.totalReturn ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{formatPercent(result.totalReturn)}</div>
  </div>
  <div class="rounded-xl border border-gray-800 bg-gray-950 p-4 text-center">
    <div class="text-xs text-gray-500">최대 낙폭</div>
    <div class="mt-2 text-2xl font-bold text-red-300">{formatPercent(result.maxDrawdown)}</div>
  </div>
  <div class="rounded-xl border border-gray-800 bg-gray-950 p-4 text-center">
    <div class="text-xs text-gray-500">샤프</div>
    <div class="mt-2 text-2xl font-bold">{Number(result.sharpeRatio ?? 0).toFixed(2)}</div>
  </div>
  <div class="rounded-xl border border-gray-800 bg-gray-950 p-4 text-center">
    <div class="text-xs text-gray-500">승률</div>
    <div class="mt-2 text-2xl font-bold">{formatPercent(result.overallWinRate, 1)}</div>
  </div>
  <div class="rounded-xl border border-gray-800 bg-gray-950 p-4 text-center">
    <div class="text-xs text-gray-500">총 거래 수</div>
    <div class="mt-2 text-2xl font-bold">{result.totalTrades ?? 0}</div>
  </div>
  <div class="rounded-xl border border-gray-800 bg-gray-950 p-4 text-center">
    <div class="text-xs text-gray-500">비용 합계</div>
    <div class="mt-2 text-lg font-bold">${formatMoney((result.totalSlippageCost ?? 0) + (result.totalCommissionCost ?? 0))}</div>
  </div>
</div>

<div class="rounded-2xl border border-gray-800 bg-gray-950 p-5">
  <div class="mb-4">
    <div class="text-sm font-semibold">위험을 감안한 성과</div>
    <div class="mt-1 text-xs text-gray-500">같은 수익률이라도 손실 변동과 낙폭이 작을수록 더 안정적인 전략입니다.</div>
  </div>
  <div class="grid grid-cols-2 gap-3 lg:grid-cols-4 xl:grid-cols-7">
    <div class="rounded border border-gray-800 bg-gray-900 p-3">
      <div class="text-xs text-gray-500">연환산 수익률</div>
      <div class="mt-1 text-lg font-semibold">{formatPercentagePoints(result.annualizedReturn)}</div>
      <div class="text-xs text-gray-500">전체 검증기간 CAGR</div>
    </div>
    <div class="rounded border border-gray-800 bg-gray-900 p-3">
      <div class="text-xs text-gray-500">하방 위험 대비 수익</div>
      <div class="mt-1 text-lg font-semibold">{Number(result.sortinoRatio ?? 0).toFixed(2)}</div>
      <div class="text-xs text-gray-500">Sortino</div>
    </div>
    <div class="rounded border border-gray-800 bg-gray-900 p-3">
      <div class="text-xs text-gray-500">낙폭 대비 수익</div>
      <div class="mt-1 text-lg font-semibold">{Number(result.calmarRatio ?? 0).toFixed(2)}</div>
      <div class="text-xs text-gray-500">Calmar</div>
    </div>
    <div class="rounded border border-gray-800 bg-gray-900 p-3">
      <div class="text-xs text-gray-500">총이익 ÷ 총손실</div>
      <div class="mt-1 text-lg font-semibold">{Number(result.profitFactor ?? 0).toFixed(2)}</div>
      <div class="text-xs text-gray-500">Profit Factor</div>
    </div>
    <div class="rounded border border-gray-800 bg-gray-900 p-3">
      <div class="text-xs text-gray-500">권장 최대 비중</div>
      <div class="mt-1 text-lg font-semibold">{formatPercent(result.halfKellyFraction, 1)}</div>
      <div class="text-xs text-gray-500">보수적 Half-Kelly</div>
    </div>
    <div class="rounded border border-gray-800 bg-gray-900 p-3">
      <div class="text-xs text-gray-500">평균 최대 역행</div>
      <div class="mt-1 text-lg font-semibold text-red-300">{formatPercent(Math.abs(Number(result.avgMaePercent ?? 0)))}</div>
      <div class="text-xs text-gray-500">진입 후 불리한 폭</div>
    </div>
    <div class="rounded border border-gray-800 bg-gray-900 p-3">
      <div class="text-xs text-gray-500">평균 최대 순행</div>
      <div class="mt-1 text-lg font-semibold text-green-300">{formatPercent(result.avgMfePercent)}</div>
      <div class="text-xs text-gray-500">진입 후 유리한 폭</div>
    </div>
  </div>
</div>

{#if timingReport}
  <div class="rounded-2xl border border-cyan-800/60 bg-cyan-950/20 p-5">
    <div class="mb-4">
      <div class="text-sm font-semibold text-cyan-200">타이밍 리포트</div>
      <div class="mt-1 text-sm text-cyan-50">기본 시나리오 대비 휩소, 낙폭, 거래 수, 곡선 안정성을 한 번에 읽을 수 있게 정리했습니다.</div>
    </div>
    <div class="grid grid-cols-1 gap-4 xl:grid-cols-4">
      <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
        <div class="text-xs text-gray-500">낙폭 절감</div>
        <div class={`mt-2 text-2xl font-bold ${Number(timingReport.drawdownImprovement ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{formatSignedPercent(timingReport.drawdownImprovement ?? 0)}</div>
      </div>
      <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
        <div class="text-xs text-gray-500">거래 수 감소</div>
        <div class={`mt-2 text-2xl font-bold ${Number(timingReport.tradeReduction ?? 0) >= 0 ? 'text-blue-200' : 'text-red-300'}`}>{timingReport.tradeReduction > 0 ? '+' : ''}{timingReport.tradeReduction}</div>
      </div>
      <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
        <div class="text-xs text-gray-500">휩소 감소</div>
        <div class={`mt-2 text-2xl font-bold ${Number(timingReport.whipsawReduction ?? 0) >= 0 ? 'text-emerald-300' : 'text-red-300'}`}>{timingReport.whipsawReduction > 0 ? '+' : ''}{timingReport.whipsawReduction}</div>
        <div class="mt-2 text-xs text-gray-400">현재 휩소 비율 {formatPercent(timingReport.currentWhipsawRate ?? 0, 1)} · 손실 단기 종료 {timingReport.currentWhipsawCount}건</div>
      </div>
      <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
        <div class="text-xs text-gray-500">곡선 안정성</div>
        <div class={`mt-2 text-2xl font-bold ${timingReport.stabilityImprovement != null && Number(timingReport.stabilityImprovement) >= 0 ? 'text-cyan-300' : 'text-red-300'}`}>{timingReport.stabilityImprovement != null ? formatSignedPercent(timingReport.stabilityImprovement) : '-'}</div>
        <div class="mt-2 text-xs text-gray-400">현재 변동성 {timingReport.currentVolatility != null ? formatPercent(timingReport.currentVolatility, 2) : '-'}</div>
      </div>
    </div>
  </div>
{/if}

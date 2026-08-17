<script>
  import { ChevronDown, Pause, Play, Trash2 } from 'lucide-svelte'
  import {
    formatDate,
    formatDuration,
    formatPercent,
    resultInsights,
    statusClass,
    summaryParams
  } from './optimizationModel'

  export let jobs = []
  export let loading = false
  export let expandedId = null
  export let jobDetails = {}
  export let jobResults = {}
  export let entryModeLabels = {}
  export let sizingModeLabels = {}
  export let onToggle = async () => {}
  export let onStateChange = async () => {}
  export let onRemove = async () => {}
  export let onSaveSettings = async () => {}
  export let onApplyResult = async () => {}
</script>

<section class="space-y-4">
  <div class="flex items-center justify-between">
    <h3 class="text-xl font-semibold">최적화 작업 목록</h3>
    <div class="text-sm text-gray-500">총 {jobs.length}개</div>
  </div>

  {#if loading}
    <div class="rounded-xl border border-gray-800 bg-gray-950 p-10 text-center text-gray-400">불러오는 중...</div>
  {:else if jobs.length === 0}
    <div class="rounded-xl border border-gray-800 bg-gray-950 p-10 text-center text-gray-400">아직 생성된 최적화 작업이 없습니다.</div>
  {:else}
    {#each jobs as job (job.id)}
      <div class="overflow-hidden rounded-2xl border border-gray-800 bg-gray-950">
        <div class="flex items-start justify-between gap-4 p-5">
          <button on:click={() => onToggle(job.id)} class="flex-1 text-left">
            <div class="mb-3 flex items-center gap-3">
              <h4 class="text-lg font-semibold">{job.name}</h4>
              <span class={`rounded px-3 py-1 text-xs ${statusClass(job.status)}`}>{job.status}</span>
            </div>
            <div class="mb-3">
              <div class="mb-1 flex justify-between text-xs text-gray-400">
                <span>{job.completedCombinations} / {job.totalCombinations} 조합</span>
                <span>{Number(job.progress ?? 0).toFixed(1)}%</span>
              </div>
              <div class="h-2 rounded-full bg-gray-800">
                <div class="h-2 rounded-full bg-blue-500 transition-all" style={`width:${Math.min(100, Number(job.progress ?? 0))}%`}></div>
              </div>
            </div>
            <div class="text-xs text-gray-500">
              생성 {formatDate(job.createdAt)}
              {#if job.startedAt} · 시작 {formatDate(job.startedAt)}{/if}
              {#if job.completedAt} · 완료 {formatDate(job.completedAt)}{/if}
            </div>
          </button>

          <div class="flex items-center gap-2">
            {#if job.status === 'Running'}
              <button on:click={() => onStateChange(job, 'pause')} class="rounded p-2 text-yellow-300 transition hover:bg-yellow-950/30" title="일시정지"><Pause size={16} /></button>
              <button on:click={() => onStateChange(job, 'cancel')} class="rounded p-2 text-red-300 transition hover:bg-red-950/30" title="취소"><Trash2 size={16} /></button>
            {:else if job.status === 'Paused'}
              <button on:click={() => onStateChange(job, 'resume')} class="rounded p-2 text-green-300 transition hover:bg-green-950/30" title="재개"><Play size={16} /></button>
              <button on:click={() => onStateChange(job, 'cancel')} class="rounded p-2 text-red-300 transition hover:bg-red-950/30" title="취소"><Trash2 size={16} /></button>
            {:else if ['Completed', 'Cancelled', 'Failed'].includes(job.status)}
              <button on:click={() => onRemove(job)} class="rounded p-2 text-red-300 transition hover:bg-red-950/30" title="삭제"><Trash2 size={16} /></button>
            {/if}
            <ChevronDown size={18} class={`text-gray-500 transition ${expandedId === job.id ? 'rotate-180' : ''}`} />
          </div>
        </div>

        {#if expandedId === job.id}
          {@const detail = jobDetails[job.id]}
          {@const results = jobResults[job.id] ?? []}
          <div class="border-t border-gray-800 bg-gray-900/40 p-5">
            {#if !detail}
              <div class="text-sm text-gray-400">상세 정보를 불러오는 중...</div>
            {:else}
              <div class="grid grid-cols-1 gap-4 xl:grid-cols-4">
                <div class="rounded-xl border border-gray-800 bg-gray-950 p-4"><div class="text-xs text-gray-500">경과 시간</div><div class="mt-2 text-lg font-semibold">{formatDuration(detail.elapsedSeconds)}</div></div>
                <div class="rounded-xl border border-gray-800 bg-gray-950 p-4"><div class="text-xs text-gray-500">예상 남은 시간</div><div class="mt-2 text-lg font-semibold">{formatDuration(detail.estimatedRemainingSeconds)}</div></div>
                <div class="rounded-xl border border-gray-800 bg-gray-950 p-4"><div class="text-xs text-gray-500">자동 반영 횟수</div><div class="mt-2 text-lg font-semibold">{detail.appliedResultCount ?? 0}</div></div>
                <div class="rounded-xl border border-gray-800 bg-gray-950 p-4"><div class="text-xs text-gray-500">마지막 진행 시간</div><div class="mt-2 text-sm font-semibold">{formatDate(detail.lastProgressAt)}</div></div>
              </div>

              {#if detail.errorMessage}<div class="mt-4 rounded-lg border border-red-700 bg-red-900/20 p-4 text-sm text-red-300">{detail.errorMessage}</div>{/if}

              <div class="mt-4 rounded-xl border border-gray-800 bg-gray-950 p-4">
                <div class="mb-3 text-sm font-semibold">자동 반영 설정</div>
                <div class="grid grid-cols-1 gap-3 xl:grid-cols-[1fr,220px,140px]">
                  <label class="flex items-center gap-2 text-sm text-gray-300"><input type="checkbox" bind:checked={detail.autoApplyBestResult} /> 완료 후 최고 결과 자동 반영</label>
                  <input type="number" bind:value={detail.autoApplyMinTrades} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="최소 거래 수" />
                  <button on:click={() => onSaveSettings(job.id)} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">저장</button>
                </div>
                {#if detail.lastAutoApplyMessage}<div class="mt-3 text-sm text-gray-400">{detail.lastAutoApplyMessage}</div>{/if}
              </div>

              <div class="mt-5 rounded-xl border border-gray-800 bg-gray-950 p-4">
                <div class="mb-4 text-sm font-semibold">상위 결과</div>
                {#if results.length === 0}
                  <div class="text-sm text-gray-400">아직 결과가 없습니다.</div>
                {:else}
                  <div class="space-y-3">
                    {#each results as result}
                      <div class="rounded-lg border border-gray-800 bg-gray-900 p-4">
                        <div class="flex flex-wrap items-start justify-between gap-4">
                          <div>
                            <div class="flex items-center gap-3"><div class="text-lg font-semibold">#{result.rank}</div><div class="text-sm text-gray-400">{summaryParams(result, entryModeLabels, sizingModeLabels)}</div></div>
                            <div class="mt-3 flex flex-wrap gap-4 text-sm">
                              <span>수익률 <strong class="text-green-300">{formatPercent(result.totalReturn)}</strong></span><span>샤프 <strong>{Number(result.sharpeRatio ?? 0).toFixed(2)}</strong></span><span>소르티노 <strong>{Number(result.sortinoRatio ?? 0).toFixed(2)}</strong></span><span>MDD <strong class="text-red-300">{formatPercent(result.maxDrawdown, 2)}</strong></span><span>승률 <strong>{formatPercent(result.winRate)}</strong></span><span>거래 수 <strong>{result.tradeCount}</strong></span>
                            </div>
                            {#if result.oosTotalReturn != null}<div class="mt-2 text-xs text-gray-400">OOS 수익률 {formatPercent(result.oosTotalReturn)} · OOS 샤프 {Number(result.oosSharpeRatio ?? 0).toFixed(2)} · OOS 거래 {result.oosTotalTrades ?? 0}</div>{/if}
                            <div class="mt-4 grid grid-cols-1 gap-3 xl:grid-cols-4">
                              {#each resultInsights(result, results) as insight}
                                <div class="rounded border border-gray-800 bg-gray-950 p-3"><div class="text-xs text-gray-500">{insight.label}</div><div class={`mt-2 text-lg font-semibold ${insight.tone}`}>{insight.value}</div><div class="mt-1 text-xs text-gray-400">{insight.description}</div></div>
                              {/each}
                            </div>
                          </div>
                          <button on:click={() => onApplyResult(job.id, result.id ?? null)} class="rounded bg-green-600 px-4 py-2 text-sm text-white transition hover:bg-green-700">이 결과 반영</button>
                        </div>
                      </div>
                    {/each}
                  </div>
                {/if}
              </div>
            {/if}
          </div>
        {/if}
      </div>
    {/each}
  {/if}
</section>

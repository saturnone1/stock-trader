<script>
  import { onMount } from 'svelte'
  import { mlApi } from '../api/endpoints'

  let loading = true
  let training = false
  let error = ''
  let message = ''
  let data = null

  onMount(load)

  function pct(value, digits = 2) {
    return (Number(value ?? 0) * 100).toFixed(digits)
  }

  async function load() {
    loading = true
    try {
      const res = await mlApi.status()
      data = res.data
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || 'ML 상태를 불러오지 못했습니다.'
    } finally {
      loading = false
    }
  }

  async function train() {
    training = true
    try {
      const res = await mlApi.train()
      message = res.data?.Message ?? res.data?.message ?? '학습을 시작했습니다.'
      await load()
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '학습 실행에 실패했습니다.'
    } finally {
      training = false
    }
  }
</script>

<div class="flex-1 overflow-auto p-8">
  <div class="flex items-center justify-between mb-8">
    <h2 class="text-4xl font-bold">ML 분석</h2>
    <div class="flex gap-3">
      <button on:click={load} class="bg-gray-700 hover:bg-gray-600 px-4 py-2 rounded transition text-sm">새로고침</button>
      <button on:click={train} disabled={training} class="bg-blue-600 hover:bg-blue-700 disabled:opacity-50 px-4 py-2 rounded transition text-sm">{training ? '학습 중...' : '모델 학습'}</button>
    </div>
  </div>

  {#if message}
    <div class="mb-6 rounded-lg border border-green-700 bg-green-900/20 p-4 text-green-300">{message}</div>
  {/if}
  {#if error}
    <div class="mb-6 rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
  {/if}

  {#if loading || !data}
    <div class="text-gray-400">불러오는 중...</div>
  {:else}
    <div class="grid grid-cols-3 gap-4 mb-8">
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Training</div><div class={`text-2xl font-bold ${data.IsTraining ? 'text-yellow-300' : 'text-green-300'}`}>{data.IsTraining ? 'RUNNING' : 'IDLE'}</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Regime Model</div><div class="text-2xl font-bold">{data.RegimeClassifier?.IsRegimeModelLoaded ? 'Loaded' : 'Missing'}</div></div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-5"><div class="text-gray-400 text-sm">Signal Scorer</div><div class="text-2xl font-bold">{data.SignalScorer?.IsSignalScorerLoaded ? 'Loaded' : 'Missing'}</div></div>
    </div>

    <div class="grid grid-cols-2 gap-6">
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-6">
        <h3 class="font-bold mb-4">Regime Classifier</h3>
        <div class="space-y-2 text-sm">
          <div class="flex justify-between"><span class="text-gray-400">Samples</span><span>{data.RegimeClassifier?.RegimeTrainingSamples ?? 0}</span></div>
          <div class="flex justify-between"><span class="text-gray-400">Trained At</span><span>{data.RegimeClassifier?.TrainedAt ?? '-'}</span></div>
        </div>
      </div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-6">
        <h3 class="font-bold mb-4">Signal Scorer</h3>
        <div class="space-y-2 text-sm">
          <div class="flex justify-between"><span class="text-gray-400">Samples</span><span>{data.SignalScorer?.SignalScorerTrainingSamples ?? 0}</span></div>
          <div class="flex justify-between"><span class="text-gray-400">Accuracy</span><span>{pct(data.SignalScorer?.SignalScorerAccuracy ?? 0)}%</span></div>
          <div class="flex justify-between"><span class="text-gray-400">AUC</span><span>{(data.SignalScorer?.SignalScorerAuc ?? 0).toFixed(3)}</span></div>
        </div>
      </div>
    </div>
  {/if}
</div>

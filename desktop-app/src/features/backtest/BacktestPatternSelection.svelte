<script>
  import { Play } from 'lucide-svelte'

  export let patterns = []
  export let selectedPatternIds = []
  export let loading = false
  export let running = false
  export let runStatus = ''
  export let onToggle
  export let onRun
</script>

<div class="mt-6 rounded-xl border border-gray-800 bg-gray-900 p-5">
  <div class="mb-4 text-sm font-semibold text-white">패턴 선택</div>
  {#if loading}
    <div class="text-sm text-gray-400">패턴을 불러오는 중...</div>
  {:else if patterns.length === 0}
    <div class="text-sm text-gray-400">저장된 커스텀 패턴이 없습니다.</div>
  {:else}
    <div class="grid grid-cols-1 gap-3 xl:grid-cols-3">
      {#each patterns as pattern}
        <label class={`rounded-lg border p-4 text-sm transition ${selectedPatternIds.includes(String(pattern.id)) ? 'border-blue-500 bg-blue-950/20' : 'border-gray-800 bg-gray-950 hover:border-gray-700'}`}>
          <div class="flex items-start gap-3"><input type="checkbox" checked={selectedPatternIds.includes(String(pattern.id))} on:change={() => onToggle(pattern.id)} class="mt-1" /><div><div class="font-medium text-white">{pattern.name}</div><div class="mt-1 text-xs text-gray-400">{pattern.description || '설명 없음'}</div></div></div>
        </label>
      {/each}
    </div>
  {/if}
</div>

<div class="mt-6 flex justify-end">
  <button on:click={onRun} disabled={running || loading} class="flex items-center gap-2 rounded bg-green-600 px-5 py-3 text-sm font-semibold text-white transition hover:bg-green-700 disabled:opacity-50"><Play size={16} />{running ? (runStatus || '실행 중...') : '백테스트 실행'}</button>
</div>

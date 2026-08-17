<script>
  import { CircleHelp, FolderTree, Plus, Trash2 } from 'lucide-svelte'

  export let patterns = []
  export let selectedPattern = null
  export let loading = false
  export let showNewPattern = false
  export let newPatternName = ''
  export let tooltipFor
  export let createPattern
  export let selectPattern
  export let deletePattern
</script>

<aside class="flex w-80 shrink-0 flex-col border-r border-gray-800 bg-gray-950">
    <div class="border-b border-gray-800 p-6">
      <div class="mb-2 flex items-center gap-3">
        <FolderTree size={20} class="text-blue-400" />
        <h2 class="text-2xl font-bold">내 매매 전략</h2>
        <span title={tooltipFor('workspace')} class="cursor-help text-gray-500 transition hover:text-blue-300">
          <CircleHelp size={16} />
        </span>
      </div>
      <p class="text-sm text-gray-400">언제 사고, 얼마나 사고, 언제 팔지 순서대로 정합니다.</p>
    </div>

    <div class="border-b border-gray-800 p-4">
      {#if !showNewPattern}
        <button on:click={() => (showNewPattern = true)} class="flex w-full items-center justify-center gap-2 rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">
          <Plus size={16} />
          새 전략
        </button>
      {:else}
        <div class="space-y-2">
          <input bind:value={newPatternName} placeholder="전략 이름" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-sm text-white" />
          <div class="flex gap-2">
            <button on:click={createPattern} class="flex-1 rounded bg-green-600 px-3 py-2 text-sm text-white transition hover:bg-green-700">생성</button>
            <button on:click={() => (showNewPattern = false)} class="flex-1 rounded bg-gray-700 px-3 py-2 text-sm text-white transition hover:bg-gray-600">취소</button>
          </div>
        </div>
      {/if}
    </div>

    <div class="flex-1 overflow-y-auto p-4">
      <div class="mb-3 text-xs uppercase tracking-wider text-gray-500">저장한 전략</div>
      {#if loading}
        <div class="text-sm text-gray-400">불러오는 중...</div>
      {:else}
        <div class="space-y-2">
          {#each patterns as pat}
            <div class={`rounded-lg border p-3 ${selectedPattern?.id === pat.id ? 'border-blue-600 bg-blue-950/30' : 'border-gray-800 bg-gray-900'}`}>
              <button on:click={() => selectPattern(pat)} class="w-full text-left">
                <div class="font-medium text-white">{pat.name}</div>
                <div class="mt-1 text-xs text-gray-500">{pat.raw?.updatedAt ?? pat.updatedAt}</div>
              </button>
              {#if String(pat.id) !== '-1001'}
                <div class="mt-2 flex justify-end">
                  <button on:click={() => deletePattern(pat)} class="rounded p-1 text-red-400 transition hover:bg-red-950/30" aria-label={`${pat.name} 전략 삭제`}>
                    <Trash2 size={14} />
                  </button>
                </div>
              {:else}
                <div class="mt-2 text-right text-[11px] text-blue-300">기본 예시 · 저장하면 내 전략으로 복사</div>
              {/if}
            </div>
          {/each}
        </div>
      {/if}
    </div>
    <div class="border-t border-gray-800 p-4 text-xs text-gray-500">
      가운데 매매 규칙에서 조건을 선택하면 오른쪽에서 수치를 바꿀 수 있습니다.
    </div>
  </aside>


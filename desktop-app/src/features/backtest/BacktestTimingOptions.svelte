<script>
  export let timingLab
  export let structureOptions = []
  export let windowOptions = []
  export let onToggleStructure
  export let onToggleWindow
</script>

<div class="mt-5 grid grid-cols-1 gap-4 xl:grid-cols-2">
  <div class="rounded-xl border border-gray-800 bg-gray-900 p-5">
    <div class="mb-3 text-sm font-semibold text-white">비교 구조</div>
    <div class="space-y-3">
      {#each structureOptions as option}
        <label class="flex items-center gap-3 rounded border border-gray-800 bg-gray-950 px-4 py-3 text-sm text-gray-300"><input type="checkbox" checked={timingLab.selectedStructures.includes(option.id)} on:change={() => onToggleStructure(option.id)} disabled={!timingLab.enabled} />{option.label}</label>
      {/each}
    </div>
  </div>

  <div class="rounded-xl border border-gray-800 bg-gray-900 p-5">
    <div class="mb-3 text-sm font-semibold text-white">비교 기간 조합</div>
    <div class="grid grid-cols-2 gap-3">
      {#each windowOptions as option}
        <label class="flex items-center gap-3 rounded border border-gray-800 bg-gray-950 px-4 py-3 text-sm text-gray-300"><input type="checkbox" checked={timingLab.selectedWindows.includes(option.id)} on:change={() => onToggleWindow(option.id)} disabled={!timingLab.enabled} />{option.label}</label>
      {/each}
    </div>
  </div>
</div>

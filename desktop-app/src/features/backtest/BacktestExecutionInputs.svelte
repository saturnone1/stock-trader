<script>
  import { TriangleAlert } from 'lucide-svelte'

  export let form
  export let timeFrameOptions = []
  export let dataSourceOptions = []
  export let slippageOptions = []
  export let warning = ''
</script>

<div class="grid grid-cols-1 gap-4 xl:grid-cols-5">
  <label class="text-sm text-gray-300 xl:col-span-2"><div class="mb-2 text-gray-500">종목</div><input bind:value={form.symbolsText} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="AAPL, MSFT, NVDA" /></label>
  <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">시작일</div><input type="date" bind:value={form.from} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
  <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">종료일</div><input type="date" bind:value={form.to} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
  <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">초기 자본금</div><input type="number" bind:value={form.initialCapital} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
</div>

<div class="mt-5 grid grid-cols-1 gap-4 xl:grid-cols-5">
  <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">타임프레임</div><select bind:value={form.timeFrame} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">{#each timeFrameOptions as [value, label]}<option value={value}>{label}</option>{/each}</select></label>
  <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">데이터 소스</div><select bind:value={form.dataSource} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">{#each dataSourceOptions as [value, label]}<option value={value}>{label}</option>{/each}</select></label>
  <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">슬리피지 모델</div><select bind:value={form.slippageModel} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">{#each slippageOptions as [value, label]}<option value={value}>{label}</option>{/each}</select></label>
  <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">슬리피지 %</div><input type="number" step="0.01" bind:value={form.slippagePercent} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
  <label class="text-sm text-gray-300"><div class="mb-2 text-gray-500">거래당 수수료</div><input type="number" step="0.1" bind:value={form.commissionPerTrade} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
</div>

{#if warning}
  <div class="mt-4 rounded-lg border border-yellow-700 bg-yellow-900/20 p-4 text-sm text-yellow-300"><div class="flex items-center gap-2"><TriangleAlert size={16} />{warning}</div></div>
{/if}

<script>
  import { ChevronDown } from 'lucide-svelte'
  import BacktestExecutionInputs from './BacktestExecutionInputs.svelte'
  import BacktestPatternSelection from './BacktestPatternSelection.svelte'
  import BacktestRiskSettings from './BacktestRiskSettings.svelte'

  export let form
  export let timeFrameOptions = []
  export let dataSourceOptions = []
  export let slippageOptions = []
  export let warning = ''
  export let patterns = []
  export let selectedPatternIds = []
  export let loading = false
  export let running = false
  export let runStatus = ''
  export let showRiskSettings = false
  export let onTogglePattern = () => {}
  export let onRun = () => {}
</script>

<section class="rounded-2xl border border-blue-800/70 bg-gray-950 p-6 shadow-lg shadow-blue-950/10">
  <div class="mb-5">
    <h3 class="text-xl font-semibold">기본 백테스트</h3>
    <p class="mt-2 text-sm text-gray-400">종목, 기간, 비용과 전략만 확인하면 바로 실행할 수 있습니다.</p>
  </div>

  <BacktestExecutionInputs {form} {timeFrameOptions} {dataSourceOptions} {slippageOptions} {warning} />

  <button on:click={() => showRiskSettings = !showRiskSettings} class="mt-5 flex items-center gap-2 text-sm text-gray-300 hover:text-white">
    <ChevronDown size={16} class={showRiskSettings ? 'rotate-180' : ''} />
    {showRiskSettings ? '리스크 설정 접기' : '리스크 설정 보기'}
  </button>
  {#if showRiskSettings}<BacktestRiskSettings {form} />{/if}

  <BacktestPatternSelection
    {patterns}
    {selectedPatternIds}
    {loading}
    {running}
    {runStatus}
    onToggle={onTogglePattern}
    {onRun}
  />
</section>

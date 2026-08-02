<script>
  import { onMount } from 'svelte'
  import { settingsApi } from '../api/endpoints'

  let loading = true
  let error = ''
  let message = ''
  let form = null

  onMount(load)

  async function load() {
    loading = true
    try {
      const { data } = await settingsApi.get()
      form = {
        id: data.Id,
        orderMode: data.OrderMode ?? 'SignalOnly',
        preferredDataSource: data.PreferredDataSource ?? 'YahooFinance',
        enabledPatterns: [...(data.EnabledPatterns ?? [])],
        watchlistSymbols: data.WatchlistSymbols ?? '',
        soundAlerts: !!data.SoundAlerts,
        accountSize: data.AccountSize ?? 100000,
        riskPerTradePercent: data.RiskPerTradePercent ?? 0.01,
        dailyLossLimitPercent: data.DailyLossLimitPercent ?? 0.03,
        maxTotalPositions: data.MaxTotalPositions ?? 7,
        maxPositionsPerSector: data.MaxPositionsPerSector ?? 2,
        minExpectancy: data.MinExpectancy ?? 0,
      }
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '설정을 불러오지 못했습니다.'
    } finally {
      loading = false
    }
  }

  async function save() {
    try {
      await settingsApi.update({
        id: form.id,
        orderMode: form.orderMode,
        preferredDataSource: form.preferredDataSource,
        enabledPatterns: form.enabledPatterns,
        watchlistSymbols: form.watchlistSymbols,
        soundAlerts: form.soundAlerts,
        accountSize: Number(form.accountSize),
        riskPerTradePercent: Number(form.riskPerTradePercent),
        dailyLossLimitPercent: Number(form.dailyLossLimitPercent),
        maxTotalPositions: Number(form.maxTotalPositions),
        maxPositionsPerSector: Number(form.maxPositionsPerSector),
        minExpectancy: Number(form.minExpectancy),
      })
      message = '설정을 저장했습니다.'
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '설정 저장에 실패했습니다.'
    }
  }
</script>

<div class="flex-1 overflow-auto p-8">
  <div class="flex items-center justify-between mb-8">
    <h2 class="text-4xl font-bold">설정</h2>
    <div class="flex gap-3">
      <button on:click={load} class="bg-gray-700 hover:bg-gray-600 px-4 py-2 rounded transition text-sm">새로고침</button>
      <button on:click={save} class="bg-blue-600 hover:bg-blue-700 px-4 py-2 rounded transition text-sm">저장</button>
    </div>
  </div>

  {#if message}
    <div class="mb-6 rounded-lg border border-green-700 bg-green-900/20 p-4 text-green-300">{message}</div>
  {/if}
  {#if error}
    <div class="mb-6 rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
  {/if}

  {#if loading || !form}
    <div class="text-gray-400">불러오는 중...</div>
  {:else}
    <div class="grid grid-cols-2 gap-6">
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-6 space-y-4">
        <h3 class="font-bold">거래 설정</h3>
        <div><label for="settings-order-mode" class="block text-sm text-gray-400 mb-2">Order Mode</label><input id="settings-order-mode" bind:value={form.orderMode} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
        <div><label for="settings-data-source" class="block text-sm text-gray-400 mb-2">Data Source</label><input id="settings-data-source" bind:value={form.preferredDataSource} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
        <div><label for="settings-watchlist" class="block text-sm text-gray-400 mb-2">Watchlist Symbols</label><input id="settings-watchlist" bind:value={form.watchlistSymbols} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
      </div>
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-6 space-y-4">
        <h3 class="font-bold">리스크 설정</h3>
        <div><label for="settings-account-size" class="block text-sm text-gray-400 mb-2">Account Size</label><input id="settings-account-size" bind:value={form.accountSize} type="number" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
        <div><label for="settings-risk-per-trade" class="block text-sm text-gray-400 mb-2">Risk / Trade</label><input id="settings-risk-per-trade" bind:value={form.riskPerTradePercent} type="number" step="0.001" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
        <div><label for="settings-daily-loss-limit" class="block text-sm text-gray-400 mb-2">Daily Loss Limit</label><input id="settings-daily-loss-limit" bind:value={form.dailyLossLimitPercent} type="number" step="0.001" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
        <div class="grid grid-cols-2 gap-4">
          <div><label for="settings-max-positions" class="block text-sm text-gray-400 mb-2">Max Positions</label><input id="settings-max-positions" bind:value={form.maxTotalPositions} type="number" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
          <div><label for="settings-max-sector" class="block text-sm text-gray-400 mb-2">Per Sector</label><input id="settings-max-sector" bind:value={form.maxPositionsPerSector} type="number" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
        </div>
        <div><label for="settings-min-expectancy" class="block text-sm text-gray-400 mb-2">Min Expectancy</label><input id="settings-min-expectancy" bind:value={form.minExpectancy} type="number" step="0.001" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
      </div>
    </div>
  {/if}
</div>

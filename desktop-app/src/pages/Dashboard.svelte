<script>
  import { onMount } from 'svelte'
  import { TrendingUp, TrendingDown, AlertCircle, Activity } from 'lucide-svelte'
  import { dashboardApi, optimizationApi, patternApi } from '../api/endpoints'

  let dashboard = null
  let patterns = []
  let jobs = []
  let loading = true
  let error = null
  let refreshInterval = null

  onMount(async () => {
    await loadDashboard()
    refreshInterval = setInterval(loadDashboard, 5000) // Refresh every 5s
    return () => clearInterval(refreshInterval)
  })

  async function loadDashboard() {
    try {
      const [dashData, pats, jobsList] = await Promise.all([
        dashboardApi.get(),
        patternApi.list(),
        optimizationApi.list()
      ])
      dashboard = dashData.data
      patterns = pats.data || []
      jobs = jobsList.data || []
      error = null
    } catch (e) {
      error = e.message || 'Failed to load dashboard'
      console.error('Dashboard load error:', e)
    } finally {
      loading = false
    }
  }

  function getStatusColor(status) {
    switch (status) {
      case 'Running':
        return 'text-yellow-400'
      case 'Completed':
        return 'text-green-400'
      case 'Failed':
        return 'text-red-400'
      default:
        return 'text-gray-400'
    }
  }

  function getStatusBg(status) {
    switch (status) {
      case 'Running':
        return 'bg-yellow-900/30 border-yellow-700'
      case 'Completed':
        return 'bg-green-900/30 border-green-700'
      case 'Failed':
        return 'bg-red-900/30 border-red-700'
      default:
        return 'bg-gray-800 border-gray-700'
    }
  }
</script>

<div class="flex-1 overflow-auto">
  <div class="p-8">
    <div class="flex justify-between items-center mb-8">
      <h2 class="text-4xl font-bold">Dashboard</h2>
      <button
        on:click={loadDashboard}
        class="bg-blue-600 hover:bg-blue-700 px-4 py-2 rounded transition text-sm"
      >
        ⟲ Refresh
      </button>
    </div>

    {#if loading}
      <div class="text-gray-400 text-center py-12">Loading...</div>
    {:else if error}
      <div class="bg-red-900/20 border border-red-700 p-6 rounded-lg text-red-400 flex items-center gap-3">
        <AlertCircle size={20} />
        <span>{error}</span>
      </div>
    {:else}
      <!-- Account Info Section -->
      {#if dashboard?.accountInfo}
        <div class="grid grid-cols-4 gap-6 mb-8">
          <div class="bg-gray-800 border border-gray-700 p-6 rounded-lg">
            <div class="text-gray-400 text-sm mb-2">Account ID</div>
            <div class="text-lg font-mono">{dashboard.accountInfo.accountId}</div>
          </div>
          <div class="bg-gray-800 border border-gray-700 p-6 rounded-lg">
            <div class="text-gray-400 text-sm mb-2">Balance</div>
            <div class="text-2xl font-bold text-green-400">
              ${dashboard.accountInfo.balance.toLocaleString('en-US', { maximumFractionDigits: 0 })}
            </div>
          </div>
          <div class="bg-gray-800 border border-gray-700 p-6 rounded-lg">
            <div class="text-gray-400 text-sm mb-2">Available</div>
            <div class="text-2xl font-bold">
              ${dashboard.accountInfo.availableBalance.toLocaleString('en-US', { maximumFractionDigits: 0 })}
            </div>
          </div>
          <div class="bg-gray-800 border border-gray-700 p-6 rounded-lg">
            <div class="text-gray-400 text-sm mb-2">Equity</div>
            <div class="text-2xl font-bold text-blue-400">
              ${dashboard.accountInfo.equity.toLocaleString('en-US', { maximumFractionDigits: 0 })}
            </div>
          </div>
        </div>
      {/if}

      <div class="grid grid-cols-2 gap-6 mb-8">
        <!-- Risk State -->
        {#if dashboard?.riskState}
          <div class="bg-gray-800 border border-gray-700 p-6 rounded-lg">
            <h3 class="text-xl font-bold mb-4">Risk Management</h3>
            <div class="space-y-4">
              <div>
                <div class="text-gray-400 text-sm mb-2">Total Exposure</div>
                <div class="text-2xl font-bold">{(dashboard.riskState.totalExposure * 100).toFixed(1)}%</div>
              </div>
              <div>
                <div class="text-gray-400 text-sm mb-2">Max Drawdown</div>
                <div class="text-2xl font-bold text-red-400">{(dashboard.riskState.maxDrawdown * 100).toFixed(2)}%</div>
              </div>
              <div>
                <div class="text-gray-400 text-sm mb-2">Risk Level</div>
                <div class={`text-lg font-bold ${dashboard.riskState.riskLevel === 'LOW' ? 'text-green-400' : dashboard.riskState.riskLevel === 'MEDIUM' ? 'text-yellow-400' : 'text-red-400'}`}>
                  {dashboard.riskState.riskLevel}
                </div>
              </div>
            </div>
          </div>
        {/if}

        <!-- Market Regime -->
        {#if dashboard?.marketRegime}
          <div class="bg-gray-800 border border-gray-700 p-6 rounded-lg">
            <h3 class="text-xl font-bold mb-4">Market Regime</h3>
            <div class="text-center py-8">
              <div class="text-3xl font-bold text-blue-400 mb-2">{dashboard.marketRegime}</div>
              <Activity size={32} class="mx-auto text-blue-400" />
            </div>
          </div>
        {/if}
      </div>

      <!-- Positions -->
      {#if dashboard?.positions && dashboard.positions.length > 0}
        <div class="bg-gray-800 border border-gray-700 p-6 rounded-lg mb-8">
          <h3 class="text-xl font-bold mb-4">Open Positions ({dashboard.positions.length})</h3>
          <div class="overflow-x-auto">
            <table class="w-full text-sm">
              <thead class="border-b border-gray-700">
                <tr class="text-gray-400">
                  <th class="text-left py-3 px-4">Symbol</th>
                  <th class="text-right py-3 px-4">Qty</th>
                  <th class="text-right py-3 px-4">Avg Price</th>
                  <th class="text-right py-3 px-4">Current</th>
                  <th class="text-right py-3 px-4">P&L</th>
                  <th class="text-right py-3 px-4">%</th>
                </tr>
              </thead>
              <tbody>
                {#each dashboard.positions as pos}
                  <tr class="border-t border-gray-700 hover:bg-gray-700/30 transition">
                    <td class="py-3 px-4 font-mono text-blue-400">{pos.symbol}</td>
                    <td class="py-3 px-4 text-right">{pos.quantity}</td>
                    <td class="py-3 px-4 text-right">${pos.avgPrice.toFixed(2)}</td>
                    <td class="py-3 px-4 text-right">${pos.currentPrice.toFixed(2)}</td>
                    <td class="py-3 px-4 text-right">
                      <span class={pos.pnl >= 0 ? 'text-green-400' : 'text-red-400'}>
                        ${pos.pnl.toFixed(2)}
                      </span>
                    </td>
                    <td class="py-3 px-4 text-right">
                      <div class="flex items-center justify-end gap-1">
                        {#if pos.pnlPercent >= 0}
                          <TrendingUp size={16} class="text-green-400" />
                          <span class="text-green-400">{(pos.pnlPercent * 100).toFixed(2)}%</span>
                        {:else}
                          <TrendingDown size={16} class="text-red-400" />
                          <span class="text-red-400">{(pos.pnlPercent * 100).toFixed(2)}%</span>
                        {/if}
                      </div>
                    </td>
                  </tr>
                {/each}
              </tbody>
            </table>
          </div>
        </div>
      {/if}

      <!-- Active Optimization Jobs -->
      {#if jobs && jobs.length > 0}
        <div class="bg-gray-800 border border-gray-700 p-6 rounded-lg mb-8">
          <h3 class="text-xl font-bold mb-4">Active Optimization Jobs</h3>
          <div class="space-y-3">
            {#each jobs.filter(j => j.status !== 'Completed' && j.status !== 'Failed') as job}
              <div class={`border p-4 rounded-lg ${getStatusBg(job.status)}`}>
                <div class="flex justify-between items-start mb-3">
                  <div>
                    <div class="font-bold">{job.name}</div>
                    <div class="text-xs text-gray-500">Job #{job.id}</div>
                  </div>
                  <div class={`text-sm font-mono ${getStatusColor(job.status)}`}>{job.status}</div>
                </div>
                <div class="mb-2">
                  <div class="text-xs text-gray-400 mb-1">
                    {job.completedCombinations || 0} / {job.totalCombinations || 0} combinations
                  </div>
                  <div class="w-full bg-gray-700 rounded h-2">
                    <div
                      class="bg-blue-500 h-2 rounded transition-all"
                      style="width: {job.progress || 0}%"
                    ></div>
                  </div>
                </div>
              </div>
            {/each}
          </div>
        </div>
      {/if}

      <!-- Patterns Summary -->
      {#if patterns && patterns.length > 0}
        <div class="bg-gray-800 border border-gray-700 p-6 rounded-lg">
          <h3 class="text-xl font-bold mb-4">Patterns ({patterns.length})</h3>
          <div class="grid grid-cols-3 gap-4">
            {#each patterns.slice(0, 6) as pat}
              <div class="bg-gray-700/50 border border-gray-600 p-4 rounded-lg hover:bg-gray-700 transition cursor-pointer">
                <div class="font-bold text-sm mb-1">{pat.name}</div>
                <div class="text-xs text-gray-400">{pat.description || 'No description'}</div>
                <div class="text-xs text-gray-500 mt-2">{pat.rules?.length || 0} rules</div>
              </div>
            {/each}
          </div>
          {#if patterns.length > 6}
            <div class="text-center mt-4 text-sm text-gray-400">
              +{patterns.length - 6} more patterns
            </div>
          {/if}
        </div>
      {/if}
    {/if}
  </div>
</div>

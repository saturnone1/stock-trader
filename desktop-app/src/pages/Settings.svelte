<script>
  import { onMount } from 'svelte'
  import { settingsApi } from '../api/endpoints'
  import {
    buildSettingsRequest,
    createSettingsForm,
    setPatternEnabled,
    validateSettingsForm
  } from '../features/settings/settingsModel'

  let loading = true
  let saving = false
  let error = ''
  let message = ''
  let form = null

  onMount(load)

  function errorText(value, fallback) {
    const errors = value?.response?.data?.errors
    if (Array.isArray(errors) && errors.length) return errors.join(' ')
    return value?.response?.data?.error || value?.message || fallback
  }

  async function load() {
    loading = true
    try {
      const { data } = await settingsApi.get()
      form = createSettingsForm(data)
      error = ''
    } catch (value) {
      error = errorText(value, '설정을 불러오지 못했습니다.')
    } finally {
      loading = false
    }
  }

  async function save() {
    const errors = validateSettingsForm(form)
    if (errors.length) {
      error = errors.join(' ')
      message = ''
      return
    }

    saving = true
    try {
      const { data } = await settingsApi.update(buildSettingsRequest(form))
      message = data?.message ?? '설정을 저장했습니다.'
      error = ''
      await load()
    } catch (value) {
      error = errorText(value, '설정 저장에 실패했습니다.')
      message = ''
    } finally {
      saving = false
    }
  }

  function togglePattern(code, enabled) {
    form = setPatternEnabled(form, code, enabled)
  }

  function selectedOrderDescription() {
    return form?.orderModes.find((item) => item.code === form.orderMode)?.description ?? ''
  }
</script>

<div class="flex-1 overflow-auto p-8">
  <div class="mb-8 flex items-center justify-between">
    <div>
      <h2 class="text-4xl font-bold">운영 설정</h2>
      <p class="mt-2 text-sm text-gray-400">실시간 감시 대상과 주문 위험 한도를 관리합니다.</p>
    </div>
    <div class="flex gap-3">
      <button on:click={load} disabled={loading || saving} class="rounded bg-gray-700 px-4 py-2 text-sm transition hover:bg-gray-600 disabled:opacity-50">새로고침</button>
      <button on:click={save} disabled={loading || saving || !form} class="rounded bg-blue-600 px-4 py-2 text-sm transition hover:bg-blue-700 disabled:opacity-50">{saving ? '저장 중...' : '저장'}</button>
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
    <div class="grid grid-cols-1 gap-6 xl:grid-cols-2">
      <section class="space-y-5 rounded-lg border border-gray-700 bg-gray-800 p-6">
        <div>
          <h3 class="font-bold">주문과 시세</h3>
          <p class="mt-1 text-xs text-gray-500">지원되는 값만 선택할 수 있습니다.</p>
        </div>
        <div>
          <label for="settings-order-mode" class="mb-2 block text-sm text-gray-300">주문 실행 방식</label>
          <select id="settings-order-mode" bind:value={form.orderMode} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each form.orderModes as option}
              <option value={option.code}>{option.label}</option>
            {/each}
          </select>
          <p class="mt-2 text-xs text-amber-300">{selectedOrderDescription()}</p>
        </div>
        <div>
          <label for="settings-data-source" class="mb-2 block text-sm text-gray-300">기본 시세 공급자</label>
          <select id="settings-data-source" bind:value={form.preferredDataSource} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each form.dataProviders as option}
              <option value={option.code}>{option.label}</option>
            {/each}
          </select>
        </div>
        <div>
          <label for="settings-watchlist" class="mb-2 block text-sm text-gray-300">관심종목</label>
          <textarea id="settings-watchlist" bind:value={form.watchlistText} rows="3" placeholder="예: SPY, QQQ, TQQQ" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white"></textarea>
          <p class="mt-2 text-xs text-gray-500">쉼표나 줄바꿈으로 구분합니다. 이 목록은 실시간 스캔 대상이며, 패턴 빌더 미리보기는 빌더에서 지정한 종목을 사용합니다.</p>
        </div>
        <label class="flex items-center gap-3 text-sm text-gray-300">
          <input type="checkbox" bind:checked={form.soundAlerts} class="h-4 w-4" />
          화면 소리 알림 사용
        </label>
      </section>

      <section class="space-y-4 rounded-lg border border-gray-700 bg-gray-800 p-6">
        <div>
          <h3 class="font-bold">위험 한도</h3>
          <p class="mt-1 text-xs text-gray-500">비율은 소수로 입력합니다. 예: 0.01 = 1%</p>
        </div>
        <div><label for="settings-account-size" class="mb-2 block text-sm text-gray-300">포지션 계산 기준 금액</label><input id="settings-account-size" bind:value={form.accountSize} type="number" min="1" step="1000" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
        <div class="grid grid-cols-2 gap-4">
          <div><label for="settings-risk-per-trade" class="mb-2 block text-sm text-gray-300">거래당 손실 허용률</label><input id="settings-risk-per-trade" bind:value={form.riskPerTradePercent} type="number" min="0.001" max="1" step="0.001" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
          <div><label for="settings-daily-loss-limit" class="mb-2 block text-sm text-gray-300">일일 손실 한도</label><input id="settings-daily-loss-limit" bind:value={form.dailyLossLimitPercent} type="number" min="0.001" max="1" step="0.001" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
        </div>
        <div class="grid grid-cols-2 gap-4">
          <div><label for="settings-max-positions" class="mb-2 block text-sm text-gray-300">전체 최대 보유 수</label><input id="settings-max-positions" bind:value={form.maxTotalPositions} type="number" min="1" step="1" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
          <div><label for="settings-max-sector" class="mb-2 block text-sm text-gray-300">업종별 최대 보유 수</label><input id="settings-max-sector" bind:value={form.maxPositionsPerSector} type="number" min="1" step="1" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
        </div>
        <div><label for="settings-min-expectancy" class="mb-2 block text-sm text-gray-300">최소 기대값</label><input id="settings-min-expectancy" bind:value={form.minExpectancy} type="number" min="0" step="0.001" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></div>
      </section>

      <section class="space-y-4 rounded-lg border border-gray-700 bg-gray-800 p-6 xl:col-span-2">
        <div>
          <h3 class="font-bold">실시간 감시할 내장 전략</h3>
          <p class="mt-1 text-xs text-gray-500">사용자 전략의 실시간 연결 여부는 패턴 빌더에서 각각 설정합니다.</p>
        </div>
        <div class="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
          {#each form.patterns as pattern}
            <label class="flex items-center gap-3 rounded border border-gray-700 bg-gray-900/60 px-4 py-3 text-sm text-gray-200">
              <input
                type="checkbox"
                checked={form.enabledPatterns.includes(pattern.code)}
                on:change={(event) => togglePattern(pattern.code, event.currentTarget.checked)}
                class="h-4 w-4"
              />
              <span>{pattern.label}</span>
            </label>
          {/each}
        </div>
      </section>
    </div>
  {/if}
</div>

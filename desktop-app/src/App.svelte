<script>
  import { onMount } from 'svelte'
  import Navigation from './lib/Navigation.svelte'
  import Guide from './pages/Guide.svelte'
  import Recommendations from './pages/Recommendations.svelte'
  import PatternStats from './pages/PatternStats.svelte'
  import PatternBuilder from './pages/PatternBuilder.svelte'
  import History from './pages/History.svelte'
  import Portfolio from './pages/Portfolio.svelte'
  import Optimization from './pages/Optimization.svelte'
  import Backtest from './pages/Backtest.svelte'
  import Ml from './pages/Ml.svelte'
  import Accounts from './pages/Accounts.svelte'
  import Settings from './pages/Settings.svelte'
  import Account from './pages/Account.svelte'
  import { authApi } from './api/endpoints'

  let currentPage = 'guide'
  let authLoading = true
  let isAuthenticated = false
  let hasUsers = true
  let allowRegistration = false
  let authError = ''
  let username = ''
  let password = ''
  let confirmPassword = ''

  function handleNav(e) {
    currentPage = e.detail
  }

  onMount(async () => {
    await checkSession()
  })

  async function checkSession() {
    try {
      const bootstrap = await authApi.bootstrap()
      hasUsers = !!bootstrap.data?.hasUsers
      allowRegistration = !!bootstrap.data?.allowRegistration

      const { data } = await authApi.me()
      isAuthenticated = !!data?.authenticated
      authError = ''
    } catch {
      isAuthenticated = false
    } finally {
      authLoading = false
    }
  }

  async function login() {
    authError = ''

    try {
      await authApi.login(username, password)
      password = ''
      await checkSession()
    } catch (error) {
      authError = error?.response?.data?.error || error?.message || '로그인에 실패했습니다.'
      isAuthenticated = false
      authLoading = false
    }
  }

  async function register() {
    authError = ''

    if (!username.trim()) {
      authError = '아이디를 입력하세요.'
      return
    }

    if (password.length < 8) {
      authError = '비밀번호는 최소 8자 이상이어야 합니다.'
      return
    }

    if (password !== confirmPassword) {
      authError = '비밀번호 확인이 일치하지 않습니다.'
      return
    }

    try {
      await authApi.register(username, password)
      confirmPassword = ''
      await login()
    } catch (error) {
      authError = error?.response?.data?.error || error?.message || '사용자 생성에 실패했습니다.'
      isAuthenticated = false
      authLoading = false
    }
  }
</script>

{#if authLoading}
  <div class="h-screen bg-gray-900 text-gray-100 flex items-center justify-center">
    <div class="text-gray-400">세션 확인 중...</div>
  </div>
{:else if !isAuthenticated}
  <div class="h-screen bg-gray-950 text-gray-100 flex items-center justify-center p-6">
    <div class="w-full max-w-md bg-gray-900 border border-gray-800 rounded-2xl p-8 shadow-2xl">
        <h1 class="text-2xl font-bold mb-2">Stock Trader Desktop</h1>
        <p class="text-gray-400 mb-6">
          {#if !hasUsers}
            최초 사용자 계정을 생성하세요.
          {:else}
            로그인 후 전략 가이드에서 `패턴 빌더 → 백테스트 → 최적화` 흐름부터 시작하세요.
          {/if}
        </p>

      {#if authError}
        <div class="mb-4 rounded-lg border border-red-800 bg-red-950/40 px-4 py-3 text-sm text-red-300">
          {authError}
        </div>
      {/if}

      <div class="space-y-4">
        <div>
          <label for="login-username" class="block text-sm text-gray-400 mb-2">아이디</label>
          <input
            id="login-username"
            bind:value={username}
            class="w-full rounded-lg border border-gray-700 bg-gray-800 px-4 py-3 text-white"
            on:keydown={(e) => e.key === 'Enter' && login()}
          />
        </div>

        <div>
          <label for="login-password" class="block text-sm text-gray-400 mb-2">비밀번호</label>
          <input
            id="login-password"
            type="password"
            bind:value={password}
            class="w-full rounded-lg border border-gray-700 bg-gray-800 px-4 py-3 text-white"
            on:keydown={(e) => e.key === 'Enter' && login()}
          />
        </div>

        {#if !hasUsers}
          <div>
            <label for="login-password-confirm" class="block text-sm text-gray-400 mb-2">비밀번호 확인</label>
            <input
              id="login-password-confirm"
              type="password"
              bind:value={confirmPassword}
              class="w-full rounded-lg border border-gray-700 bg-gray-800 px-4 py-3 text-white"
              on:keydown={(e) => e.key === 'Enter' && register()}
            />
          </div>
        {/if}

        <button
          on:click={hasUsers ? login : register}
          class="w-full rounded-lg bg-blue-600 px-4 py-3 font-semibold hover:bg-blue-700 transition"
        >
          {#if hasUsers}로그인{:else}최초 사용자 생성{/if}
        </button>
      </div>
    </div>
  </div>
{:else}
  <div class="flex h-screen bg-gray-900 text-gray-100">
    <Navigation on:navigate={handleNav} {currentPage} />
    <main class="flex-1 overflow-auto">
      {#if currentPage === 'guide'}
        <Guide />
      {:else if currentPage === 'recommendations'}
        <Recommendations />
      {:else if currentPage === 'pattern-stats'}
        <PatternStats />
      {:else if currentPage === 'patterns'}
        <PatternBuilder />
      {:else if currentPage === 'history'}
        <History />
      {:else if currentPage === 'portfolio'}
        <Portfolio />
      {:else if currentPage === 'optimization'}
        <Optimization />
      {:else if currentPage === 'backtest'}
        <Backtest />
      {:else if currentPage === 'ml'}
        <Ml />
      {:else if currentPage === 'accounts'}
        <Accounts />
      {:else if currentPage === 'settings'}
        <Settings />
      {:else if currentPage === 'account'}
        <Account />
      {/if}
    </main>
  </div>
{/if}

<style>
  :global(body) {
    margin: 0;
    padding: 0;
    overflow: hidden;
  }
  :global(*) {
    box-sizing: border-box;
  }
</style>

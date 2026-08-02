<script>
  import { authApi } from '../api/endpoints'

  let message = ''
  let error = ''
  let currentPassword = ''
  let newPassword = ''
  let confirmPassword = ''
  let saving = false

  async function changePassword() {
    message = ''
    error = ''

    if (newPassword.length < 8) {
      error = '새 비밀번호는 최소 8자 이상이어야 합니다.'
      return
    }

    if (newPassword !== confirmPassword) {
      error = '새 비밀번호 확인이 일치하지 않습니다.'
      return
    }

    saving = true
    try {
      const { data } = await authApi.changePassword(currentPassword, newPassword)
      message = data?.message ?? '비밀번호가 변경되었습니다.'
      currentPassword = ''
      newPassword = ''
      confirmPassword = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '비밀번호 변경에 실패했습니다.'
    } finally {
      saving = false
    }
  }
</script>

<div class="flex-1 overflow-auto p-8">
  <div class="mb-8">
    <h2 class="text-4xl font-bold">계정 관리</h2>
    <p class="mt-2 text-gray-400">비밀번호 변경과 계정 보안 관리</p>
  </div>

  {#if message}
    <div class="mb-6 rounded-lg border border-green-700 bg-green-900/20 p-4 text-green-300">{message}</div>
  {/if}
  {#if error}
    <div class="mb-6 rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
  {/if}

  <div class="max-w-2xl rounded-lg border border-gray-700 bg-gray-800 p-6 space-y-4">
    <div>
      <label for="account-current-password" class="block text-sm text-gray-400 mb-2">현재 비밀번호</label>
      <input id="account-current-password" bind:value={currentPassword} type="password" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
    </div>
    <div>
      <label for="account-new-password" class="block text-sm text-gray-400 mb-2">새 비밀번호</label>
      <input id="account-new-password" bind:value={newPassword} type="password" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
    </div>
    <div>
      <label for="account-confirm-password" class="block text-sm text-gray-400 mb-2">새 비밀번호 확인</label>
      <input id="account-confirm-password" bind:value={confirmPassword} type="password" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
    </div>
    <div class="text-sm text-gray-500">최소 8자 이상으로 설정하세요.</div>
    <div>
      <button on:click={changePassword} disabled={saving} class="rounded bg-blue-600 px-4 py-2 text-white transition hover:bg-blue-700 disabled:opacity-50">
        {saving ? '변경 중...' : '비밀번호 변경'}
      </button>
    </div>
  </div>
</div>

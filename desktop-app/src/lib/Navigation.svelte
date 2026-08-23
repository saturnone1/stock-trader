<script>
  import { createEventDispatcher } from 'svelte'
  import { BarChart3, LayoutTemplate, Zap, Settings, BarChart2, Lightbulb, Briefcase, History, Landmark, UserRound, BookOpen } from 'lucide-svelte'
  
  export let currentPage
  const dispatch = createEventDispatcher()

  const navSections = [
    {
      title: '연구 시작',
      items: [
        { id: 'guide', label: '전략 가이드', icon: BookOpen },
      ]
    },
    {
      title: '보조 도구',
      items: [
        { id: 'recommendations', label: '종목 추천', icon: Lightbulb },
      ]
    },
    {
      title: '핵심 연구 흐름',
      items: [
        { id: 'patterns', label: '전략 만들기', icon: LayoutTemplate },
        { id: 'backtest', label: '백테스트', icon: BarChart2 },
        { id: 'optimization', label: '수치 다듬기', icon: Zap },
        { id: 'pattern-stats', label: '패턴 통계', icon: BarChart3 },
      ]
    },
    {
      title: '포트폴리오 관리 전략',
      items: [
        { id: 'portfolio', label: '포트폴리오', icon: Briefcase },
      ]
    },
    {
      title: '통계 및 계좌 관리',
      items: [
        { id: 'history', label: '거래 내역', icon: History },
        { id: 'accounts', label: '계좌 관리', icon: Landmark },
        { id: 'account', label: '사용자 계정', icon: UserRound },
      ]
    },
    {
      title: '설정',
      items: [
        { id: 'settings', label: '설정', icon: Settings },
      ]
    }
  ]
</script>

<nav class="w-64 bg-gray-950 border-r border-gray-800 flex flex-col">
  <div class="p-6 border-b border-gray-800">
    <h1 class="text-xl font-bold">Stock Trader</h1>
  </div>

  <div class="flex-1 overflow-y-auto p-4 space-y-6">
    {#each navSections as section}
      <div>
        <div class="px-4 pb-2 text-xs uppercase tracking-wider text-gray-500">{section.title}</div>
        <ul class="space-y-2">
          {#each section.items as item}
            <li>
              <button
                on:click={() => dispatch('navigate', item.id)}
                class={`w-full flex items-center gap-3 px-4 py-3 rounded transition ${
                  currentPage === item.id
                    ? 'bg-blue-600 text-white'
                    : 'text-gray-400 hover:bg-gray-800'
                }`}
              >
                <svelte:component this={item.icon} size={20} />
                {item.label}
              </button>
            </li>
          {/each}
        </ul>
      </div>
    {/each}
  </div>
</nav>

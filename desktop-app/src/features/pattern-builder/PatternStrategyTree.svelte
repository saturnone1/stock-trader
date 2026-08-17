<script>
  import { ArrowDown, ArrowUp, CircleHelp, Copy, Trash2 } from 'lucide-svelte'

  export let workspace
  export let selectedNode
  export let timeFrameOptions = []
  export let tooltipFor
  export let selectNode
  export let displayEntryMode
  export let displaySizingMode
  export let displayLogic
  export let displayScalingDirection
  export let ruleSummary
  export let addNode
  export let addRuleToGroup
  export let addRuleToExitGroup
  export let moveNode
  export let duplicateNode
  export let removeNode
  export let addTierCondition
  export let addScalingCondition
</script>

        <div class="mb-6 rounded-xl border border-gray-800 bg-gray-950 p-5">
          <button on:click={() => selectNode({ type: 'general' })} class={`w-full text-left ${selectedNode.type === 'general' ? 'text-blue-300' : 'text-white'}`}>
            <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
              <span title={tooltipFor('pattern')} class="cursor-help">전략 기본 설정</span>
              <span title={tooltipFor('pattern')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                <CircleHelp size={12} />
              </span>
            </div>
            <div class="mt-1 text-xl font-semibold">{workspace.name}</div>
            <div class="mt-2 flex flex-wrap gap-2 text-xs">
              <span class="rounded bg-gray-800 px-2 py-1">매수 시점: {displayEntryMode(workspace.entryMode)}</span>
              <span class="rounded bg-gray-800 px-2 py-1">기준 봉: {timeFrameOptions.find((item) => item.value === workspace.timeFrame)?.label ?? workspace.timeFrame}</span>
              <span class="rounded bg-gray-800 px-2 py-1">주문 금액: {displaySizingMode(workspace.sizingMode)}</span>
              <span class="rounded bg-gray-800 px-2 py-1">{workspace.isActive ? '연구 사용 중' : '연구 제외'}</span>
              <span class={`rounded px-2 py-1 ${workspace.enableLiveTrading ? 'bg-amber-900/50 text-amber-200' : 'bg-gray-800'}`}>{workspace.enableLiveTrading ? '실시간 주문 연결' : '실시간 주문 꺼짐'}</span>
              <span class="rounded bg-gray-800 px-2 py-1">{workspace.requireBullRegime ? '강세장만 허용' : '장세 무관'}</span>
            </div>
          </button>
        </div>

        <div class="space-y-5">
          <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
            <div class="mb-4 flex items-center justify-between">
              <button on:click={() => selectNode({ type: 'entryRoot' })} class="text-left">
                <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                  <span title={tooltipFor('entryGroup')} class="cursor-help">언제 살까?</span>
                  <span title={tooltipFor('entryGroup')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                    <CircleHelp size={12} />
                  </span>
                </div>
                <div class="text-lg font-semibold">매수 상황 중 {displayLogic(workspace.entryGroupsLogic)}</div>
              </button>
              <button on:click={() => addNode('group')} class="rounded bg-gray-800 px-3 py-1 text-xs text-white transition hover:bg-gray-700">+ 매수 상황</button>
            </div>

            <div class="space-y-3">
              {#each workspace.entryGroups as group, groupIndex}
                <div class="rounded-lg border border-gray-800 bg-gray-900 p-4">
                  <div class="mb-3 flex items-center justify-between">
                    <button on:click={() => selectNode({ type: 'group', groupIndex })} class={`text-left ${selectedNode.type === 'group' && selectedNode.groupIndex === groupIndex ? 'text-blue-300' : 'text-white'}`}>
                      <div class="font-semibold">{group.label || `매수 상황 ${groupIndex + 1}`}</div>
                      <div class="mt-1 text-xs text-gray-500">조건을 {displayLogic(group.logic)} • {group.rules.length}개</div>
                    </button>
                    <div class="flex items-center gap-2">
                      <button on:click={() => addRuleToGroup({})} class="rounded bg-gray-800 px-2 py-1 text-xs text-white transition hover:bg-gray-700">+ 매수 조건</button>
                      <button title="위로" on:click={() => moveNode({ type: 'group', groupIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                      <button title="아래로" on:click={() => moveNode({ type: 'group', groupIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                      <button title="복제" on:click={() => duplicateNode({ type: 'group', groupIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                      <button on:click={() => removeNode({ type: 'group', groupIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                    </div>
                  </div>
                  <div class="space-y-2 border-l border-gray-800 pl-4">
                    {#each group.rules as rule, ruleIndex}
                      <div class="flex items-center gap-1">
                        <button on:click={() => selectNode({ type: 'entryRule', groupIndex, ruleIndex })} class={`min-w-0 flex-1 rounded border px-3 py-3 text-left text-sm transition ${selectedNode.type === 'entryRule' && selectedNode.groupIndex === groupIndex && selectedNode.ruleIndex === ruleIndex ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-950 text-gray-200 hover:border-gray-700'}`}>
                          <div title={tooltipFor('rule')} class="text-xs text-gray-400 cursor-help">매수 조건 {ruleIndex + 1}</div>
                          <div class="mt-1">{ruleSummary(rule)}</div>
                        </button>
                        <button title="위로" on:click={() => moveNode({ type: 'entryRule', groupIndex, ruleIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                        <button title="아래로" on:click={() => moveNode({ type: 'entryRule', groupIndex, ruleIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                        <button title="복제" on:click={() => duplicateNode({ type: 'entryRule', groupIndex, ruleIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                        <button title="삭제" on:click={() => removeNode({ type: 'entryRule', groupIndex, ruleIndex })} class="rounded p-1 text-red-400 hover:bg-red-950/30"><Trash2 size={13} /></button>
                      </div>
                    {/each}
                  </div>
                </div>
              {/each}
            </div>
          </div>

          <div class="grid grid-cols-2 gap-5">
            <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
              <div class="mb-4 flex items-center justify-between">
                <button on:click={() => selectNode({ type: 'exitRoot' })} class="text-left">
                  <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                    <span title={tooltipFor('exitRule')} class="cursor-help">언제 팔까?</span>
                    <span title={tooltipFor('exitRule')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                      <CircleHelp size={12} />
                    </span>
                  </div>
                  <div class="text-lg font-semibold">매도 상황 중 {displayLogic(workspace.exitGroupsLogic)}</div>
                </button>
                <button on:click={() => addNode('exitGroup')} class="rounded bg-gray-800 px-3 py-1 text-xs text-white transition hover:bg-gray-700">+ 매도 상황</button>
              </div>
              <div class="space-y-3">
                {#each workspace.exitGroups as group, groupIndex}
                  <div class="rounded-lg border border-gray-800 bg-gray-900 p-3">
                    <div class="mb-2 flex items-center justify-between gap-2">
                      <button on:click={() => selectNode({ type: 'exitGroup', groupIndex })} class={`text-left ${selectedNode.type === 'exitGroup' && selectedNode.groupIndex === groupIndex ? 'text-blue-300' : 'text-white'}`}>
                        <div class="font-semibold">{group.label || `매도 상황 ${groupIndex + 1}`}</div>
                        <div class="text-xs text-gray-500">조건을 {displayLogic(group.logic)} • {group.rules.length}개</div>
                      </button>
                      <div class="flex gap-2">
                        <button on:click={() => { selectNode({ type: 'exitGroup', groupIndex }); addRuleToExitGroup({}); }} class="rounded bg-gray-800 px-2 py-1 text-xs text-white">+ 매도 조건</button>
                        <button title="위로" on:click={() => moveNode({ type: 'exitGroup', groupIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                        <button title="아래로" on:click={() => moveNode({ type: 'exitGroup', groupIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                        <button title="복제" on:click={() => duplicateNode({ type: 'exitGroup', groupIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                        <button on:click={() => removeNode({ type: 'exitGroup', groupIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                      </div>
                    </div>
                    <div class="space-y-2 border-l border-gray-800 pl-3">
                      {#each group.rules as rule, ruleIndex}
                        <div class="flex gap-2">
                          <button on:click={() => selectNode({ type: 'exitRule', groupIndex, ruleIndex })} class={`flex-1 rounded border px-3 py-2 text-left text-sm transition ${selectedNode.type === 'exitRule' && selectedNode.groupIndex === groupIndex && selectedNode.ruleIndex === ruleIndex ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-950 text-gray-200 hover:border-gray-700'}`}>
                            {ruleSummary(rule)}
                          </button>
                          <button title="위로" on:click={() => moveNode({ type: 'exitRule', groupIndex, ruleIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                          <button title="아래로" on:click={() => moveNode({ type: 'exitRule', groupIndex, ruleIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                          <button title="복제" on:click={() => duplicateNode({ type: 'exitRule', groupIndex, ruleIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                          <button on:click={() => removeNode({ type: 'exitRule', groupIndex, ruleIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/each}
              </div>
            </div>

            <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
              <div class="mb-4 flex items-center justify-between">
                <button on:click={() => selectNode({ type: 'weightRoot' })} class="text-left">
                  <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                    <span title={tooltipFor('weightTier')} class="cursor-help">얼마나 살까?</span>
                    <span title={tooltipFor('weightTier')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                      <CircleHelp size={12} />
                    </span>
                  </div>
                  <div class="text-lg font-semibold">{workspace.useWeightTiers ? '사용 중' : '사용 안 함'}</div>
                </button>
                <button on:click={() => addNode('weightTier')} class="rounded bg-gray-800 px-3 py-1 text-xs text-white transition hover:bg-gray-700">+ 매수 비중</button>
              </div>
              <div class="space-y-2">
                {#each workspace.weightTiers as tier, tierIndex}
                  <div class="rounded-lg border border-gray-800 bg-gray-900 p-3">
                    <div class="mb-2 flex items-center justify-between">
                      <button on:click={() => selectNode({ type: 'weightTier', tierIndex })} class={`text-left ${selectedNode.type === 'weightTier' && selectedNode.tierIndex === tierIndex ? 'text-blue-300' : 'text-white'}`}>
                        <div class="font-semibold">{tier.label}</div>
                        <div class="text-xs text-gray-500">{displayLogic(tier.logic)} • {tier.allocationPercent}%</div>
                      </button>
                      <div class="flex gap-2">
                        <button on:click={() => addTierCondition(tierIndex)} class="rounded bg-gray-800 px-2 py-1 text-xs text-white">+ 적용 조건</button>
                        <button title="위로" on:click={() => moveNode({ type: 'weightTier', tierIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                        <button title="아래로" on:click={() => moveNode({ type: 'weightTier', tierIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                        <button title="복제" on:click={() => duplicateNode({ type: 'weightTier', tierIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                        <button on:click={() => removeNode({ type: 'weightTier', tierIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                      </div>
                    </div>
                    <div class="space-y-2 border-l border-gray-800 pl-3">
                      {#each tier.conditions as rule, ruleIndex}
                        <div class="flex gap-2">
                          <button on:click={() => selectNode({ type: 'tierRule', tierIndex, ruleIndex })} class={`flex-1 rounded border px-3 py-2 text-left text-sm transition ${selectedNode.type === 'tierRule' && selectedNode.tierIndex === tierIndex && selectedNode.ruleIndex === ruleIndex ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-950 text-gray-200 hover:border-gray-700'}`}>{ruleSummary(rule)}</button>
                          <button title="위로" on:click={() => moveNode({ type: 'tierRule', tierIndex, ruleIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                          <button title="아래로" on:click={() => moveNode({ type: 'tierRule', tierIndex, ruleIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                          <button title="복제" on:click={() => duplicateNode({ type: 'tierRule', tierIndex, ruleIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                          <button on:click={() => removeNode({ type: 'tierRule', tierIndex, ruleIndex })} class="rounded p-1 text-red-400 hover:bg-red-950/30"><Trash2 size={14} /></button>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/each}
              </div>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-5">
            <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
              <div class="mb-4 flex items-center justify-between">
                <button on:click={() => selectNode({ type: 'scalingRoot' })} class="text-left">
                  <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                    <span title={tooltipFor('scalingRule')} class="cursor-help">추가 매수·분할 매도</span>
                    <span title={tooltipFor('scalingRule')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                      <CircleHelp size={12} />
                    </span>
                  </div>
                  <div class="text-lg font-semibold">설정 {workspace.scalingRules.length}개</div>
                </button>
                <button on:click={() => addNode('scalingRule')} class="rounded bg-gray-800 px-3 py-1 text-xs text-white transition hover:bg-gray-700">+ 추가 매수·매도</button>
              </div>
              <div class="space-y-2">
                {#each workspace.scalingRules as rule, scalingIndex}
                  <div class="rounded-lg border border-gray-800 bg-gray-900 p-3">
                    <div class="mb-2 flex items-center justify-between">
                      <button on:click={() => selectNode({ type: 'scalingRule', scalingIndex })} class={`text-left ${selectedNode.type === 'scalingRule' && selectedNode.scalingIndex === scalingIndex ? 'text-blue-300' : 'text-white'}`}>
                        <div class="font-semibold">{displayScalingDirection(rule.direction)}</div>
                        <div class="text-xs text-gray-500">{displayLogic(rule.logic)} • {rule.percent}% • 최대 {rule.maxCount}회</div>
                      </button>
                      <div class="flex gap-2">
                        <button on:click={() => addScalingCondition(scalingIndex)} class="rounded bg-gray-800 px-2 py-1 text-xs text-white">+ 실행 조건</button>
                        <button title="위로" on:click={() => moveNode({ type: 'scalingRule', scalingIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                        <button title="아래로" on:click={() => moveNode({ type: 'scalingRule', scalingIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                        <button title="복제" on:click={() => duplicateNode({ type: 'scalingRule', scalingIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                        <button on:click={() => removeNode({ type: 'scalingRule', scalingIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                      </div>
                    </div>
                    <div class="space-y-2 border-l border-gray-800 pl-3">
                      {#each rule.conditions as condition, ruleIndex}
                        <div class="flex gap-2">
                          <button on:click={() => selectNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex })} class={`flex-1 rounded border px-3 py-2 text-left text-sm transition ${selectedNode.type === 'scalingRuleCondition' && selectedNode.scalingIndex === scalingIndex && selectedNode.ruleIndex === ruleIndex ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-950 text-gray-200 hover:border-gray-700'}`}>{ruleSummary(condition)}</button>
                          <button title="위로" on:click={() => moveNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                          <button title="아래로" on:click={() => moveNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                          <button title="복제" on:click={() => duplicateNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                          <button on:click={() => removeNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex })} class="rounded p-1 text-red-400 hover:bg-red-950/30"><Trash2 size={14} /></button>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/each}
              </div>
            </div>

            <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
              <div class="mb-4 flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                <span title={tooltipFor('runtime')} class="cursor-help">거래 제한·안전장치</span>
                <span title={tooltipFor('runtime')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                  <CircleHelp size={12} />
                </span>
              </div>
              <div class="space-y-2">
                <button on:click={() => selectNode({ type: 'timeFilter' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'timeFilter' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>매매 가능 시기</button>
                <button on:click={() => selectNode({ type: 'circuitBreaker' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'circuitBreaker' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>손실 시 거래 중단</button>
                <button on:click={() => selectNode({ type: 'reentry' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'reentry' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>다시 매수하기까지 대기</button>
                <button on:click={() => selectNode({ type: 'portfolioRules' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'portfolioRules' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>보유 종목·비중 한도</button>
                <button on:click={() => selectNode({ type: 'dynamicExit' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'dynamicExit' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>손절·목표가 계산법</button>
              </div>
            </div>
          </div>
        </div>

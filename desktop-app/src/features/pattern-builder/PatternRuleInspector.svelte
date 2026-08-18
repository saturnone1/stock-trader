<script>
  import { CircleHelp, Trash2 } from 'lucide-svelte'

  export let workspace
  export let selectedNode
  export let tooltipFor
  export let touch
  export let timeFrameOptions = []
  export let entryModeOptions = []
  export let sizingModeOptions = []
  export let logicOptions = []
  export let scalingDirectionOptions = []
  export let stopTypeOptions = []
  export let targetTypeOptions = []
  export let dayOptions = []
  export let monthOptions = []
  export let indicatorPalette = []
  export let operatorOptions = []
  export let operatorLabels = {}
  export let indicatorValueGuides = {}
  export let displayEntryMode
  export let displaySizingMode
  export let displayLogic
  export let displayScalingDirection
  export let displayStopType
  export let displayTargetType
  export let addRuleToGroup
  export let addRuleToExitGroup
  export let addTierCondition
  export let addScalingCondition
  export let toggleListValue
  export let setDynamicExitType
  export let getDynamicFieldConfigs
  export let updateDynamicParam
  export let getCurrentRule
  export let updateRuleField
  export let getIndicatorFieldConfigs
  export let getExtraParamEntries
  export let addRuleMapEntry
  export let updateRuleMapEntry
  export let removeRuleMapEntry
</script>

<aside class="w-[30rem] shrink-0 overflow-y-auto bg-gray-950 p-6">
    {#if !workspace}
      <div class="text-gray-400">전략을 선택하면 세부 설정이 열립니다.</div>
    {:else if selectedNode.type === 'general' || selectedNode.type === 'entryRoot' || selectedNode.type === 'exitRoot' || selectedNode.type === 'weightRoot' || selectedNode.type === 'scalingRoot'}
      <div class="space-y-5">
        <div>
          <div class="mb-2 flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
            <span title={tooltipFor('pattern')} class="cursor-help">전략 세부 설정</span>
            <span title={tooltipFor('pattern')} class="cursor-help text-gray-600 transition hover:text-blue-300">
              <CircleHelp size={12} />
            </span>
          </div>
          <input bind:value={workspace.name} on:input={touch} placeholder="전략 이름" class="mb-3 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
          <textarea bind:value={workspace.description} on:input={touch} rows="3" placeholder="전략 설명" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white"></textarea>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">전략 기준 봉</div>
            <select bind:value={workspace.timeFrame} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each timeFrameOptions as option}<option value={option.value}>{option.label}</option>{/each}
            </select>
            <div class="mt-2 text-xs text-gray-500">미리보기와 백테스트에 같은 봉을 사용합니다.</div>
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div title={tooltipFor('entryMode')} class="mb-2 cursor-help text-gray-500">언제 주문할까요?</div>
            <select bind:value={workspace.entryMode} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each entryModeOptions as option}<option value={option}>{displayEntryMode(option)}</option>{/each}
            </select>
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div title={tooltipFor('sizingMode')} class="mb-2 cursor-help text-gray-500">주문 금액 계산법</div>
            <select bind:value={workspace.sizingMode} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each sizingModeOptions as option}<option value={option}>{displaySizingMode(option)}</option>{/each}
            </select>
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">매수 상황이 여러 개라면</div>
            <select bind:value={workspace.entryGroupsLogic} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
            </select>
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">매도 조건이 여러 개라면</div>
            <select bind:value={workspace.exitGroupsLogic} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
            </select>
          </label>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">ATR 손절 배수</div>
            <input type="number" step="0.1" bind:value={workspace.atrStopMultiplier} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">ATR 목표 배수</div>
            <input type="number" step="0.1" bind:value={workspace.atrTargetMultiplier} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">최대 보유 봉 수</div>
            <input type="number" bind:value={workspace.maxHoldingBars} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">기본 비중 %</div>
            <input type="number" step="1" bind:value={workspace.defaultAllocationPercent} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">트레일링 ATR</div>
            <input type="number" step="0.1" bind:value={workspace.trailingAtr} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">부분 익절 R</div>
            <input type="number" step="0.1" bind:value={workspace.partialProfitR} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
        </div>

        <label class="flex items-center gap-3 rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
          <input type="checkbox" bind:checked={workspace.requireBullRegime} on:change={touch} />
          강세장일 때만 매수
        </label>
        <label class="flex items-center gap-3 rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
          <input type="checkbox" bind:checked={workspace.useWeightTiers} on:change={touch} />
          상황별 매수 비중 사용
        </label>
        <label class="flex items-center gap-3 rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
          <input type="checkbox" bind:checked={workspace.isActive} on:change={touch} />
          연구·미리보기·백테스트에서 이 전략 사용
        </label>
        <label class="block rounded border border-amber-900/60 bg-amber-950/20 p-3 text-sm text-gray-300">
          <span class="flex items-center gap-3">
            <input type="checkbox" bind:checked={workspace.enableLiveTrading} on:change={touch} />
            실시간 감시와 자동 주문에 연결
          </span>
          <span class="mt-2 block text-xs leading-5 text-amber-300/80">현재 실시간 실행은 ‘일봉 + 다음 봉 시가’ 전략과 1차 부분 익절을 지원합니다. 추가 매수·사용자 정의 분할 매도는 미리보기와 백테스트에서 검증할 수 있지만 실시간 주문은 아직 켤 수 없습니다.</span>
        </label>
      </div>
    {:else if selectedNode.type === 'group'}
      {@const group = workspace.entryGroups[selectedNode.groupIndex]}
      <div class="space-y-4">
        <div title={tooltipFor('entryGroup')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">매수 상황</div>
        <input bind:value={group.label} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        <select bind:value={group.logic} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
          {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
        </select>
        <button on:click={() => addRuleToGroup({})} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">+ 매수 조건 추가</button>
      </div>
    {:else if selectedNode.type === 'exitGroup'}
      {@const group = workspace.exitGroups[selectedNode.groupIndex]}
      <div class="space-y-4">
        <div title={tooltipFor('exitRule')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">매도 상황</div>
        <input bind:value={group.label} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        <label class="block text-sm text-gray-300">
          <div class="mb-2 text-gray-500">이 상황의 조건이 여러 개라면</div>
          <select bind:value={group.logic} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
          </select>
        </label>
        <button on:click={() => addRuleToExitGroup({})} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">+ 매도 조건 추가</button>
      </div>
    {:else if selectedNode.type === 'weightTier'}
      {@const tier = workspace.weightTiers[selectedNode.tierIndex]}
      <div class="space-y-4">
        <div title={tooltipFor('weightTier')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">매수 비중</div>
        <input bind:value={tier.label} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        <div class="grid grid-cols-2 gap-3">
          <label class="text-sm text-gray-400">조건 결합
            <select bind:value={tier.logic} on:change={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
              {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
            </select>
          </label>
          <label class="text-sm text-gray-400">투자 비중 (%)
            <input type="number" min="0" max="100" bind:value={tier.allocationPercent} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
          </label>
        </div>
        <div class="rounded border border-blue-900/60 bg-blue-950/20 p-3 text-xs leading-5 text-blue-200">위에서부터 조건을 확인해 처음 만족한 비중 하나만 적용합니다. 순서가 결과에 영향을 줍니다.</div>
        <button on:click={() => addTierCondition(selectedNode.tierIndex)} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">+ 적용 조건 추가</button>
      </div>
    {:else if selectedNode.type === 'scalingRule'}
      {@const rule = workspace.scalingRules[selectedNode.scalingIndex]}
      <div class="space-y-4">
        <div title={tooltipFor('scalingRule')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">추가 매수·분할 매도</div>
        <div class="grid grid-cols-2 gap-3">
          <label class="text-sm text-gray-400">실행 종류<select bind:value={rule.direction} on:change={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each scalingDirectionOptions as option}<option value={option}>{displayScalingDirection(option)}</option>{/each}
          </select></label>
          <label class="text-sm text-gray-400">조건 결합<select bind:value={rule.logic} on:change={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
          </select></label>
          <label class="text-sm text-gray-400">최초 매수 수량 대비 비율 (%)<input type="number" min="0" max="100" bind:value={rule.percent} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
          <label class="text-sm text-gray-400">최대 실행 횟수<input type="number" min="1" bind:value={rule.maxCount} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
          <label class="col-span-2 text-sm text-gray-400">이 수익률 이상일 때만 실행 (%)<input type="number" step="0.1" bind:value={rule.minProfitPercent} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        </div>
        <button on:click={() => addScalingCondition(selectedNode.scalingIndex)} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">+ 실행 조건 추가</button>
      </div>
    {:else if selectedNode.type === 'timeFilter'}
      <div class="space-y-4">
        <div class="text-xs uppercase tracking-wider text-gray-500">매매 가능 시기</div>
        <div class="block text-sm text-gray-300">
          <div class="mb-2 text-gray-500">매수할 요일 <span class="text-xs">(선택하지 않으면 매일)</span></div>
          <div class="grid grid-cols-7 gap-2">
            {#each dayOptions as day}
              <button type="button" on:click={() => (workspace.timeFilter.allowedDaysOfWeek = toggleListValue(workspace.timeFilter.allowedDaysOfWeek, day.value))} class={`rounded border px-2 py-2 ${workspace.timeFilter.allowedDaysOfWeek.includes(day.value) ? 'border-blue-500 bg-blue-950/50 text-blue-200' : 'border-gray-700 bg-gray-900 text-gray-400'}`}>{day.label}</button>
            {/each}
          </div>
        </div>
        <div class="block text-sm text-gray-300">
          <div class="mb-2 text-gray-500">매수하지 않을 달</div>
          <div class="grid grid-cols-6 gap-2">
            {#each monthOptions as month}
              <button type="button" on:click={() => (workspace.timeFilter.blockedMonths = toggleListValue(workspace.timeFilter.blockedMonths, month))} class={`rounded border px-2 py-2 ${workspace.timeFilter.blockedMonths.includes(month) ? 'border-rose-600 bg-rose-950/40 text-rose-200' : 'border-gray-700 bg-gray-900 text-gray-400'}`}>{month}월</button>
            {/each}
          </div>
        </div>
      </div>
    {:else if selectedNode.type === 'circuitBreaker'}
      <div class="space-y-4">
        <div class="text-xs uppercase tracking-wider text-gray-500">손실 시 거래 중단</div>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">연속 손실 허용 횟수</span><input type="number" bind:value={workspace.circuitBreaker.consecutiveLossLimit} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">거래를 멈출 봉 수</span><input type="number" bind:value={workspace.circuitBreaker.cooldownBars} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">전략 최대 낙폭 %</span><input type="number" step="0.1" bind:value={workspace.circuitBreaker.maxDrawdownPercent} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
      </div>
    {:else if selectedNode.type === 'reentry'}
      <div class="space-y-4">
        <div class="text-xs uppercase tracking-wider text-gray-500">다시 매수하기까지 대기</div>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">손실 후 대기 봉 수</span><input type="number" bind:value={workspace.reentry.cooldownBarsAfterLoss} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">수익 후 대기 봉 수</span><input type="number" bind:value={workspace.reentry.cooldownBarsAfterWin} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
      </div>
    {:else if selectedNode.type === 'portfolioRules'}
      <div class="space-y-4">
        <div class="text-xs uppercase tracking-wider text-gray-500">보유 종목·비중 한도</div>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">동시에 보유할 최대 종목 수</span><input type="number" bind:value={workspace.portfolioRules.maxTotalPositions} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">한 종목의 최대 비중 %</span><input type="number" step="0.1" bind:value={workspace.portfolioRules.maxSinglePositionPercent} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">하루 최대 매수 횟수</span><input type="number" bind:value={workspace.portfolioRules.maxEntriesPerDay} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">최대 상관계수 (백테스트)</span><input type="number" step="0.01" bind:value={workspace.portfolioRules.maxCorrelation} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
      </div>
    {:else if selectedNode.type === 'dynamicExit'}
      <div class="space-y-4">
        <div title={tooltipFor('dynamicExit')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">손절·목표가 계산법</div>
        <div class="rounded border border-gray-800 bg-gray-900 p-4">
          <div class="mb-3 text-sm font-semibold text-white">손절</div>
          <select bind:value={workspace.dynamicExit.stopType} on:change={(e) => setDynamicExitType('stop', e.currentTarget.value)} class="mb-3 w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
            {#each stopTypeOptions as option}<option value={option}>{displayStopType(option)}</option>{/each}
          </select>
          <div class="grid grid-cols-2 gap-3">
            {#each getDynamicFieldConfigs('stop', workspace.dynamicExit.stopType) as field}
              <label class="block text-sm text-gray-300">
                <div class="mb-2 text-gray-500">{field.label}</div>
                <input type="number" step={field.step} value={workspace.dynamicExit.stopParams[field.key] ?? field.defaultValue} on:input={(e) => updateDynamicParam('stop', field.key, e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
              </label>
            {/each}
          </div>
        </div>
        <div class="rounded border border-gray-800 bg-gray-900 p-4">
          <div class="mb-3 text-sm font-semibold text-white">목표가</div>
          <select bind:value={workspace.dynamicExit.targetType} on:change={(e) => setDynamicExitType('target', e.currentTarget.value)} class="mb-3 w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
            {#each targetTypeOptions as option}<option value={option}>{displayTargetType(option)}</option>{/each}
          </select>
          <div class="grid grid-cols-2 gap-3">
            {#each getDynamicFieldConfigs('target', workspace.dynamicExit.targetType) as field}
              <label class="block text-sm text-gray-300">
                <div class="mb-2 text-gray-500">{field.label}</div>
                <input type="number" step={field.step} value={workspace.dynamicExit.targetParams[field.key] ?? field.defaultValue} on:input={(e) => updateDynamicParam('target', field.key, e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
              </label>
            {/each}
          </div>
        </div>
      </div>
    {:else}
      {@const rule = getCurrentRule()}
      {#if rule}
        <div class="space-y-4">
          <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
            <span title={tooltipFor('ruleInspector')} class="cursor-help">선택한 조건 바꾸기</span>
            <span title={tooltipFor('ruleInspector')} class="cursor-help text-gray-600 transition hover:text-blue-300">
              <CircleHelp size={12} />
            </span>
          </div>
          <select bind:value={rule.indicator} on:change={(e) => updateRuleField('indicator', e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each indicatorPalette as section}
              <optgroup label={section.title}>
                {#each section.items as item}
                  <option value={item.indicator}>{item.label}</option>
                {/each}
              </optgroup>
            {/each}
          </select>
          <div class="grid grid-cols-2 gap-3">
            <select bind:value={rule.operator} on:change={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
              {#each operatorOptions as option}<option value={option}>{operatorLabels[option] ?? option}</option>{/each}
            </select>
            <label class="text-xs text-gray-400">기준값 {indicatorValueGuides[rule.indicator] ? `(${indicatorValueGuides[rule.indicator]})` : ''}<input type="number" step="0.1" bind:value={rule.value} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
            <label class="text-xs text-gray-400">최근 몇 봉 안에 한 번이라도<input type="number" min="0" bind:value={rule.withinBars} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
            <label class="text-xs text-gray-400">몇 봉 연속 만족<input type="number" min="0" bind:value={rule.consecutiveBars} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
            <label class="text-xs text-gray-400">신뢰도 계산 가중치<input type="number" min="0.1" step="0.1" bind:value={rule.weight} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
            <label class="text-xs text-gray-400">다른 종목을 기준으로 판단<input bind:value={rule.refSymbol} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 uppercase text-white" placeholder="예: SPY" /></label>
          </div>
          <div class="rounded border border-gray-800 bg-gray-900 p-3 text-xs text-gray-400">
            조건이 최근에 한 번이라도 나왔는지는 <span class="text-gray-200">최근 몇 봉 안에</span>, 계속 이어져야 한다면 <span class="text-gray-200">연속 만족</span>을 사용하세요. 두 값은 동시에 사용할 수 없습니다. 가중치는 매수 여부가 아니라 신뢰도 점수에만 반영됩니다.
          </div>
          <select bind:value={rule.compareIndicator} on:change={(e) => updateRuleField('compareIndicator', e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            <option value="">고정값과 비교</option>
            {#each indicatorPalette as section}
              <optgroup label={section.title}>
                {#each section.items as item}
                  <option value={item.indicator}>{item.label}</option>
                {/each}
              </optgroup>
            {/each}
          </select>

          <div class="rounded border border-gray-800 bg-gray-900 p-4">
            <div class="mb-2 flex items-center justify-between">
              <div class="text-sm font-semibold text-white">지표 계산 설정</div>
              <button on:click={() => addRuleMapEntry('params')} class="rounded bg-gray-800 px-2 py-1 text-xs text-white">+ 고급 계산값</button>
            </div>
            {#if getIndicatorFieldConfigs(rule.indicator).length > 0}
              <div class="mb-3 grid grid-cols-2 gap-3">
                {#each getIndicatorFieldConfigs(rule.indicator) as field}
                  <label class="block text-sm text-gray-300">
                    <div class="mb-2 text-gray-500">{field.label}</div>
                    <input type="number" step={field.step} value={rule.params?.[field.key] ?? field.defaultValue} on:input={(e) => updateRuleMapEntry('params', field.key, field.key, e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  </label>
                {/each}
              </div>
            {/if}
            <div class="space-y-2">
              {#each getExtraParamEntries(rule.params, rule.indicator) as [key, value]}
                <div class="grid grid-cols-[1fr,1fr,auto] gap-2">
                  <input value={key} on:input={(e) => updateRuleMapEntry('params', key, e.currentTarget.value, value)} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  <input type="number" step="0.1" value={value} on:input={(e) => updateRuleMapEntry('params', key, key, e.currentTarget.value)} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  <button on:click={() => removeRuleMapEntry('params', key)} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                </div>
              {/each}
            </div>
          </div>

          <div class="rounded border border-gray-800 bg-gray-900 p-4">
            <div class="mb-2 flex items-center justify-between">
              <div class="text-sm font-semibold text-white">비교 지표 계산 설정</div>
              <button on:click={() => addRuleMapEntry('compareParams')} disabled={!rule.compareIndicator} class="rounded bg-gray-800 px-2 py-1 text-xs text-white disabled:opacity-40">+ 고급 계산값</button>
            </div>
            {#if rule.compareIndicator && getIndicatorFieldConfigs(rule.compareIndicator).length > 0}
              <div class="mb-3 grid grid-cols-2 gap-3">
                {#each getIndicatorFieldConfigs(rule.compareIndicator) as field}
                  <label class="block text-sm text-gray-300">
                    <div class="mb-2 text-gray-500">{field.label}</div>
                    <input type="number" step={field.step} value={rule.compareParams?.[field.key] ?? field.defaultValue} on:input={(e) => updateRuleMapEntry('compareParams', field.key, field.key, e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  </label>
                {/each}
              </div>
            {/if}
            <div class="space-y-2">
              {#each getExtraParamEntries(rule.compareParams, rule.compareIndicator) as [key, value]}
                <div class="grid grid-cols-[1fr,1fr,auto] gap-2">
                  <input value={key} on:input={(e) => updateRuleMapEntry('compareParams', key, e.currentTarget.value, value)} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  <input type="number" step="0.1" value={value} on:input={(e) => updateRuleMapEntry('compareParams', key, key, e.currentTarget.value)} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  <button on:click={() => removeRuleMapEntry('compareParams', key)} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                </div>
              {/each}
            </div>
            {#if !rule.compareIndicator}
              <div class="text-xs text-gray-500">비교 지표를 선택하면 해당 지표의 계산 설정이 여기 표시됩니다.</div>
            {/if}
          </div>
        </div>
      {/if}
    {/if}
  </aside>

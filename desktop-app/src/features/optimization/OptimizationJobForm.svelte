<script>
  import { Save, Zap } from 'lucide-svelte'
  import FinancialFactorBuilder from '../../lib/FinancialFactorBuilder.svelte'
  import UniverseBuilder from '../../lib/UniverseBuilder.svelte'
  import { estimatedCombinationCount } from './optimizationModel'

  export let form
  export let patterns = []
  export let timeFrameOptions = []
  export let dataSourceOptions = []
  export let rankOptions = []
  export let logicOptionValues = []
  export let yesNoOptions = []
  export let entryModeOptions = []
  export let sizingModeOptions = []
  export let entryRuleOptions = []
  export let exitRuleOptions = []
  export let creating = false
  export let loading = false
  export let onPatternChange = () => {}
  export let onCreate = async () => {}
</script>
<section class="rounded-2xl border border-gray-800 bg-gray-950 p-6">
      <div class="mb-5 flex items-center gap-2">
        <Zap size={18} class="text-blue-400" />
        <h3 class="text-xl font-semibold">새 최적화 작업</h3>
      </div>

      <div class="grid grid-cols-1 gap-4 xl:grid-cols-4">
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">패턴</div>
          <select bind:value={form.patternId} on:change={onPatternChange} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each patterns as pattern}
              <option value={pattern.id}>{pattern.name}</option>
            {/each}
          </select>
        </label>
        <label class="text-sm text-gray-300 xl:col-span-2">
          <div class="mb-2 text-gray-500">종목</div>
          <input bind:value={form.symbolsText} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="SPY, QQQ, TQQQ" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">작업 이름</div>
          <input bind:value={form.jobName} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="패턴 최적화" />
        </label>

        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">시작일</div>
          <input type="date" bind:value={form.from} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">종료일</div>
          <input type="date" bind:value={form.to} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">타임프레임</div>
          <select bind:value={form.timeFrame} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each timeFrameOptions as [value, label]}
              <option value={value}>{label}</option>
            {/each}
          </select>
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">데이터 소스</div>
          <select bind:value={form.dataSource} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each dataSourceOptions as [value, label]}
              <option value={value}>{label}</option>
            {/each}
          </select>
        </label>
      </div>

      <div class="mt-6">
        <FinancialFactorBuilder bind:symbolsText={form.symbolsText} title="최적화용 재무 팩터 빌더" description="가치/흑자/턴어라운드/성장 조건으로 유니버스를 먼저 고른 뒤 그 집합에 대해서만 타이밍 최적화를 돌립니다." />
      </div>

      <div class="mt-6">
        <UniverseBuilder bind:symbolsText={form.symbolsText} title="최적화용 유니버스 빌더" description="백테스트에서 효과를 본 시총/섹터 유니버스를 그대로 가져와 타이밍 최적화의 입력 종목군으로 씁니다." />
      </div>

      <div class="mt-6 grid grid-cols-1 gap-4 xl:grid-cols-5">
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">정렬 기준</div>
          <select bind:value={form.rankBy} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each rankOptions as [value, label]}
              <option value={value}>{label}</option>
            {/each}
          </select>
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">표시 결과 수</div>
          <input type="number" bind:value={form.maxResults} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">최대 조합 수</div>
          <input type="number" bind:value={form.maxCombinations} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">OOS 비율</div>
          <input type="number" step="0.05" min="0" max="0.5" bind:value={form.oosPercent} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">우선순위</div>
          <input type="number" bind:value={form.priority} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
      </div>

      <div class="mt-6 grid grid-cols-1 gap-4 xl:grid-cols-4">
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">Chunk 크기</div>
          <input type="number" bind:value={form.chunkSize} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">최대 실행 시간(시간)</div>
          <input type="number" step="0.5" bind:value={form.maxDurationHours} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="비우면 무제한" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">최대 테스트 수</div>
          <input type="number" bind:value={form.maxTestedCombinations} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="비우면 무제한" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">보관 결과 수</div>
          <input type="number" bind:value={form.topResultsToKeep} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
      </div>

      <div class="mt-6 rounded-xl border border-blue-700/40 bg-blue-950/20 p-5">
        <div class="flex items-center justify-between gap-4">
          <div>
            <div class="text-base font-semibold text-white">타이밍 전용 스윕</div>
            <div class="mt-1 text-sm text-blue-100">현재 패턴의 진입/청산 룰 기간만 먼저 좁게 최적화합니다. 구조 비교는 백테스트의 타이밍 연구실에서 끝낸 뒤 이 화면으로 넘어오는 흐름이 맞습니다.</div>
          </div>
          <label class="flex items-center gap-2 text-sm text-blue-100">
            <input type="checkbox" bind:checked={form.timingFocusMode} />
            타이밍 모드 사용
          </label>
        </div>

        <div class="mt-5 grid grid-cols-1 gap-4 xl:grid-cols-2">
          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
            <div class="mb-3 font-medium text-white">진입 기간 스윕</div>
            <select bind:value={form.selectedEntryRuleIndex} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white disabled:opacity-40" disabled={!form.timingFocusMode}>
              <option value="">선택 안 함</option>
              {#each entryRuleOptions as rule}
                <option value={rule.index}>{rule.label}</option>
              {/each}
            </select>
            <label class="mt-3 block text-sm text-gray-300">
              <div class="mb-2 text-gray-500">진입 기간 후보</div>
              <input bind:value={form.entryPeriodValuesText} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white disabled:opacity-40" placeholder="10, 20, 30" disabled={!form.timingFocusMode || form.selectedEntryRuleIndex === ''} />
            </label>
          </div>

          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
            <div class="mb-3 font-medium text-white">청산 기간 스윕</div>
            <select bind:value={form.selectedExitRuleIndex} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white disabled:opacity-40" disabled={!form.timingFocusMode}>
              <option value="">선택 안 함</option>
              {#each exitRuleOptions as rule}
                <option value={rule.index}>{rule.label}</option>
              {/each}
            </select>
            <label class="mt-3 block text-sm text-gray-300">
              <div class="mb-2 text-gray-500">청산 기간 후보</div>
              <input bind:value={form.exitPeriodValuesText} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white disabled:opacity-40" placeholder="5, 10, 20" disabled={!form.timingFocusMode || form.selectedExitRuleIndex === ''} />
            </label>
          </div>
        </div>

        <div class="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-3">
          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300">
            <div class="mb-2 font-medium text-white">함께 스윕할 옵션</div>
            <label class="mb-2 flex items-center gap-2"><input type="checkbox" bind:checked={form.sweepEntryLogic} disabled={!form.timingFocusMode} /> 진입 로직</label>
            <label class="mb-2 flex items-center gap-2"><input type="checkbox" bind:checked={form.sweepExitLogic} disabled={!form.timingFocusMode} /> 청산 로직</label>
            <label class="mb-2 flex items-center gap-2"><input type="checkbox" bind:checked={form.sweepRequireBullRegime} disabled={!form.timingFocusMode} /> 강세장 제한 on/off</label>
            <label class="mb-2 flex items-center gap-2"><input type="checkbox" bind:checked={form.sweepEntryMode} disabled={!form.timingFocusMode} /> 매수 시점 비교</label>
            <label class="flex items-center gap-2"><input type="checkbox" bind:checked={form.sweepSizingMode} disabled={!form.timingFocusMode} /> 주문 금액 방식 비교</label>
          </div>

          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300">
            <div class="mb-2 font-medium text-white">예상 조합 수</div>
            <div class="text-3xl font-bold text-blue-300">{estimatedCombinationCount(form).toLocaleString('ko-KR')}</div>
            <div class="mt-2 text-xs text-gray-500">타이밍 축과 선택한 옵션을 기준으로 대략 계산한 값입니다.</div>
          </div>

          <div class="rounded-lg border border-amber-700 bg-amber-900/10 p-4 text-sm text-amber-100">
            <div class="mb-2 font-medium text-amber-200">권장 흐름</div>
            <div>1. 백테스트에서 구조를 정합니다.</div>
            <div>2. 여기서는 진입/청산 기간만 좁게 스윕합니다.</div>
            <div>3. 손절/보유/비중 축은 아래 보조 탐색을 켰을 때만 같이 돕니다.</div>
          </div>
        </div>
      </div>

      <div class="mt-6 rounded-xl border border-gray-800 bg-gray-900 p-5">
        <div class="mb-4 flex items-center justify-between gap-4">
          <div class="text-sm font-semibold text-white">보조 리스크 / 청산 축</div>
          <label class="flex items-center gap-2 text-sm text-gray-300">
            <input type="checkbox" bind:checked={form.includeRiskExitAxes} />
            함께 탐색
          </label>
        </div>
        <div class={`grid grid-cols-1 gap-4 xl:grid-cols-3 ${form.includeRiskExitAxes ? '' : 'opacity-50'}`}>
          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
            <div class="mb-3 font-medium">손절 / 목표</div>
            <div class="grid grid-cols-3 gap-2 text-xs text-gray-300">
              <input type="number" step="0.1" bind:value={form.atrStopMin} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="손절 최소" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.atrStopMax} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="손절 최대" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.atrStopStep} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="손절 간격" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.atrTargetMin} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="목표 최소" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.atrTargetMax} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="목표 최대" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.atrTargetStep} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="목표 간격" disabled={!form.includeRiskExitAxes} />
            </div>
          </div>

          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
            <div class="mb-3 font-medium">보유 / 청산</div>
            <div class="grid grid-cols-3 gap-2 text-xs text-gray-300">
              <input type="number" bind:value={form.maxHoldingMin} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="보유 최소" disabled={!form.includeRiskExitAxes} />
              <input type="number" bind:value={form.maxHoldingMax} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="보유 최대" disabled={!form.includeRiskExitAxes} />
              <input type="number" bind:value={form.maxHoldingStep} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="보유 간격" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.trailingAtrMin} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="트레일 최소" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.trailingAtrMax} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="트레일 최대" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.trailingAtrStep} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="트레일 간격" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.partialProfitMin} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="부분익절 최소" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.partialProfitMax} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="부분익절 최대" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.partialProfitStep} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="부분익절 간격" disabled={!form.includeRiskExitAxes} />
            </div>
          </div>

          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
            <div class="mb-3 font-medium">전략 옵션</div>
            <div class="space-y-3 text-sm text-gray-300">
              <label class="block">
                <div class="mb-2 text-gray-500">기본 비중 범위</div>
                <div class="grid grid-cols-3 gap-2">
                  <input type="number" bind:value={form.defaultAllocationMin} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="최소" disabled={!form.includeRiskExitAxes} />
                  <input type="number" bind:value={form.defaultAllocationMax} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="최대" disabled={!form.includeRiskExitAxes} />
                  <input type="number" bind:value={form.defaultAllocationStep} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="간격" disabled={!form.includeRiskExitAxes} />
                </div>
              </label>
              <div class="grid grid-cols-2 gap-2">
                <div class="rounded border border-gray-800 bg-gray-900 p-3">
                  <div class="mb-2 text-xs text-gray-500">진입 로직 후보</div>
                  {#each logicOptionValues as [value, label]}
                    <label class="mb-1 flex items-center gap-2 text-xs">
                      <input type="checkbox" checked={form.entryLogicOptions.includes(value)} on:change={(e) => form.entryLogicOptions = e.currentTarget.checked ? [...form.entryLogicOptions, value] : form.entryLogicOptions.filter((item) => item !== value)} />
                      {label}
                    </label>
                  {/each}
                </div>
                <div class="rounded border border-gray-800 bg-gray-900 p-3">
                  <div class="mb-2 text-xs text-gray-500">청산 로직 후보</div>
                  {#each logicOptionValues as [value, label]}
                    <label class="mb-1 flex items-center gap-2 text-xs">
                      <input type="checkbox" checked={form.exitLogicOptions.includes(value)} on:change={(e) => form.exitLogicOptions = e.currentTarget.checked ? [...form.exitLogicOptions, value] : form.exitLogicOptions.filter((item) => item !== value)} />
                      {label}
                    </label>
                  {/each}
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div class="mt-6 grid grid-cols-1 gap-4 xl:grid-cols-3">
        <div class="rounded-lg border border-gray-800 bg-gray-900 p-4 text-sm text-gray-300">
          <div class="mb-3 font-medium text-white">장세 / 진입 방식</div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <div class="mb-2 text-xs text-gray-500">강세장 제한</div>
              {#each yesNoOptions as [value, label]}
                <label class="mb-1 flex items-center gap-2 text-xs">
                  <input type="checkbox" checked={form.requireBullRegimeOptions.includes(value)} on:change={(e) => form.requireBullRegimeOptions = e.currentTarget.checked ? [...form.requireBullRegimeOptions, value] : form.requireBullRegimeOptions.filter((item) => item !== value)} />
                  {label}
                </label>
              {/each}
            </div>
            <div>
              <div class="mb-2 text-xs text-gray-500">진입 방식</div>
              {#each entryModeOptions as [value, label]}
                <label class="mb-1 flex items-center gap-2 text-xs">
                  <input type="checkbox" checked={form.entryModeOptions.includes(value)} on:change={(e) => form.entryModeOptions = e.currentTarget.checked ? [...form.entryModeOptions, value] : form.entryModeOptions.filter((item) => item !== value)} />
                  {label}
                </label>
              {/each}
            </div>
          </div>
        </div>

        <div class="rounded-lg border border-gray-800 bg-gray-900 p-4 text-sm text-gray-300">
          <div class="mb-3 font-medium text-white">사이징 방식</div>
          {#each sizingModeOptions as [value, label]}
            <label class="mb-2 flex items-center gap-2 text-xs">
              <input type="checkbox" checked={form.sizingModeOptions.includes(value)} on:change={(e) => form.sizingModeOptions = e.currentTarget.checked ? [...form.sizingModeOptions, value] : form.sizingModeOptions.filter((item) => item !== value)} />
              {label}
            </label>
          {/each}
        </div>

        <div class="rounded-lg border border-gray-800 bg-gray-900 p-4 text-sm text-gray-300">
          <div class="mb-3 font-medium text-white">자동 운용</div>
          <label class="mb-2 flex items-center gap-2">
            <input type="checkbox" bind:checked={form.continuousMode} />
            연속 최적화 모드
          </label>
          <label class="mb-3 flex items-center gap-2">
            <input type="checkbox" bind:checked={form.autoApplyBestResult} />
            완료 후 최고 결과 자동 반영
          </label>
          <label class="block">
            <div class="mb-2 text-xs text-gray-500">자동 반영 최소 거래 수</div>
            <input type="number" bind:value={form.autoApplyMinTrades} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
        </div>
      </div>

      <div class="mt-6 flex justify-end">
        <button on:click={onCreate} disabled={creating || loading} class="flex items-center gap-2 rounded bg-green-600 px-5 py-3 text-sm font-semibold text-white transition hover:bg-green-700 disabled:opacity-50">
          <Save size={16} />
          {creating ? '생성 중...' : '최적화 작업 생성'}
        </button>
      </div>
    </section>

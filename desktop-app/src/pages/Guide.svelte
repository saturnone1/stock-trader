<div class="flex-1 overflow-auto p-8">
  <div class="mb-8">
    <h2 class="text-4xl font-bold">전략 연구 가이드</h2>
    <p class="mt-2 text-gray-400">기능을 하나씩 보는 대신, 이 프로그램을 하나의 연구 흐름으로 쓰는 방법부터 정리했습니다.</p>
  </div>

  <section class="mb-6 rounded-2xl border border-cyan-700/50 bg-cyan-950/20 p-6">
    <h3 class="text-2xl font-bold text-cyan-100">이 프로그램은 이렇게 쓰면 됩니다</h3>
    <p class="mt-2 text-sm text-cyan-50">핵심은 많지 않습니다. <span class="text-white">규칙을 만들고</span>, <span class="text-white">같은 조건에서 비교하고</span>, <span class="text-white">마지막에만 좁게 조정</span>하면 됩니다.</p>
    <div class="mt-5 grid gap-4 xl:grid-cols-3">
      <div class="rounded-xl border border-cyan-800/50 bg-gray-950/80 p-5">
        <div class="text-xs font-semibold uppercase tracking-wide text-cyan-300">1단계</div>
        <div class="mt-2 text-xl font-bold text-white">전략 만들기</div>
        <div class="mt-2 text-sm text-gray-300">내 전략의 매수·매도 조건 뼈대를 만듭니다. 여기서는 구조를 정하지, 성과를 확정하지 않습니다.</div>
      </div>
      <div class="rounded-xl border border-cyan-800/50 bg-gray-950/80 p-5">
        <div class="text-xs font-semibold uppercase tracking-wide text-cyan-300">2단계</div>
        <div class="mt-2 text-xl font-bold text-white">백테스트</div>
        <div class="mt-2 text-sm text-gray-300">언제 사고팔지, 어떤 유니버스/재무 필터가 맞는지 비교합니다. 이 앱의 중심은 여기입니다.</div>
      </div>
      <div class="rounded-xl border border-cyan-800/50 bg-gray-950/80 p-5">
        <div class="text-xs font-semibold uppercase tracking-wide text-cyan-300">3단계</div>
        <div class="mt-2 text-xl font-bold text-white">수치 다듬기</div>
        <div class="mt-2 text-sm text-gray-300">검증된 전략에서 진입·청산·손절 목표 중 한 영역만 좁게 비교합니다.</div>
      </div>
    </div>
  </section>

  <section class="mb-6 rounded-2xl border border-violet-700/40 bg-violet-950/10 p-6">
    <h3 class="text-xl font-bold text-violet-100">각 화면이 담당하는 질문</h3>
    <div class="mt-4 grid gap-4 xl:grid-cols-2">
      <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
        <div class="text-lg font-semibold text-white">무엇을 살까?</div>
        <div class="mt-2 text-sm text-gray-300">유니버스 빌더와 재무 팩터 빌더가 담당합니다. 시총, 섹터, 가치, 흑자, 턴어라운드, 성장 필터로 후보군을 좁힙니다.</div>
      </div>
      <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
        <div class="text-lg font-semibold text-white">언제 살고 팔까?</div>
        <div class="mt-2 text-sm text-gray-300">백테스트의 타이밍 연구실이 담당합니다. 기본 전략, 시장 타이밍, 시장+종목 타이밍을 같은 조건으로 비교합니다.</div>
      </div>
      <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
        <div class="text-lg font-semibold text-white">어떤 재무 조합이 더 낫나?</div>
        <div class="mt-2 text-sm text-gray-300">팩터 실험실이 담당합니다. 프리셋, 현재 빌더 조건, 커스텀 조합을 자동으로 묶어 비교합니다.</div>
      </div>
      <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
        <div class="text-lg font-semibold text-white">어디를 마지막에 다듬나?</div>
        <div class="mt-2 text-sm text-gray-300">수치 다듬기가 담당합니다. 좋은 조합을 찾은 뒤 진입·청산·손절 목표 중 한 영역만 좁게 조정합니다.</div>
      </div>
    </div>
  </section>

  <div class="grid grid-cols-2 gap-6">
    <section class="rounded-lg border border-blue-700/60 bg-blue-950/20 p-6">
      <h3 class="mb-4 text-xl font-bold text-blue-200">지금 바로 밀어도 되는 영역</h3>
      <ul class="space-y-3 text-sm text-gray-200">
        <li><span class="text-white">시장 타이밍</span>: 대칭/비대칭 진입·청산, 레짐 기반 비중 조절</li>
        <li><span class="text-white">개별 종목 타이밍</span>: 종목 자체의 20일선/추세 조건 추가</li>
        <li><span class="text-white">전략 만들기 → 백테스트 → 수치 다듬기</span>: 매수·매도 조건 설계와 검증 흐름</li>
        <li><span class="text-white">포트폴리오 비중 전략</span>: 강세/약세/과열 구간별 가중치 실험</li>
        <li><span class="text-white">시총 유니버스 빌더</span>: 시총 백분위, 섹터, 산업 기준 후보군 구성</li>
        <li><span class="text-white">재무 팩터 빌더</span>: 저PER, 저PBR, 흑자, 턴어라운드, 성장률 비교 + 자동 수집 파이프라인 + SEC 외부 동기화</li>
        <li><span class="text-white">백테스트 필터 비교</span>: 현재 입력 vs 시총/재무/교집합 필터를 타이밍 시나리오와 직접 비교</li>
        <li><span class="text-white">팩터 실험실</span>: 여러 재무 팩터 프리셋, 현재 빌더 조건, 사용자 정의 조합을 자동 생성하고 성과 랭킹/개선폭 카드로 해석</li>
      </ul>
    </section>

    <section class="rounded-lg border border-amber-700 bg-amber-900/10 p-6">
      <h3 class="mb-4 text-xl font-bold text-amber-200">아직 1급 시민이 아닌 영역</h3>
      <ul class="space-y-3 text-sm text-amber-100">
        <li><span class="text-white">분기 영업이익 흑자전환</span> 같은 더 깊은 재무 변화 팩터</li>
        <li><span class="text-white">대규모 팩터 조합 스윕</span>: 이제 사용자 정의 조합까지 가능하지만 완전 자유형 대량 탐색은 아직 제한적</li>
      </ul>
    </section>

    <section class="rounded-lg border border-gray-700 bg-gray-800 p-6">
      <h3 class="mb-4 text-xl font-bold">권장 작업 순서</h3>
      <ol class="space-y-3 text-sm text-gray-300">
        <li>1. <span class="text-white">전략 만들기</span>에서 언제 사고, 얼마나 사고, 언제 팔지 정합니다.</li>
        <li>2. <span class="text-white">유니버스 빌더 / 재무 팩터 빌더</span>에서 먼저 후보군을 정합니다. 이 단계는 "무엇을 살까"를 정리하는 단계입니다.</li>
        <li>3. <span class="text-white">백테스트의 타이밍 연구실</span>에서 현재 입력 / 시총 필터 / 재무 필터 / 교집합 필터를 같은 시나리오로 비교합니다. 이 단계가 메인 검증 단계입니다.</li>
        <li>4. <span class="text-white">팩터 실험실</span>에서 여러 재무 프리셋, 현재 빌더 조건, 사용자 정의 조합을 자동 비교하고, 최소 종목 수와 랭킹 기준으로 좁힙니다.</li>
        <li>5. <span class="text-white">수치 다듬기</span>는 마지막에만 씁니다. 진입·청산·손절 목표 중 한 영역만 보수적으로 비교합니다.</li>
      </ol>
    </section>

    <section class="rounded-lg border border-gray-700 bg-gray-800 p-6">
      <h3 class="mb-4 text-xl font-bold">처음엔 안 붙잡아도 되는 것</h3>
      <ul class="space-y-3 text-sm text-gray-300">
        <li><span class="text-white">종목 추천</span>: 처음 진입점으로 보지 말고, 보조 아이디어 소스로만 보세요.</li>
        <li><span class="text-white">포트폴리오 / 거래 내역 / 계좌 관리</span>: 연구가 아니라 운영에 가까운 화면입니다.</li>
        <li><span class="text-white">전문가 실행 설정</span>: 초반에는 건드리지 말고 기본 구조가 맞는지부터 확인하세요.</li>
      </ul>
    </section>

    <section class="rounded-lg border border-emerald-700/60 bg-emerald-950/20 p-6">
      <h3 class="mb-4 text-xl font-bold text-emerald-200">처음 시작할 때 추천 시나리오</h3>
      <ol class="space-y-3 text-sm text-emerald-50">
        <li>1. <span class="text-white">전략 하나를 단순하게 만든다</span>: 매수·매도 조건을 먼저 짧게 세웁니다.</li>
        <li>2. <span class="text-white">백테스트에서 타이밍만 비교한다</span>: 기본 vs 시장 vs 시장+종목을 20/20, 20/10 정도로 먼저 봅니다.</li>
        <li>3. <span class="text-white">후보 종목군을 좁힌다</span>: 필요하면 시총/재무 필터를 켜서 무엇을 살지 정리합니다.</li>
        <li>4. <span class="text-white">마지막에 수치를 다듬는다</span>: 잘 나온 전략에서 한 영역의 현재값 주변만 좁게 비교합니다.</li>
      </ol>
    </section>
  </div>
</div>

using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Backtest;

/// <summary>
/// fmkorea "200일 티큐 변형매매법" 포지션-비중 기반 백테스터.
/// Python compute_basic_strategy 함수를 C#으로 1:1 번역합니다.
///
/// 비중 코드:
///   0 = 0%  (현금)
///   1 = 10% (방어적 소규모 보유)
///   2 = 100% (풀포지션)
///   3 = 90%  (과열 단계1 감량)
///   4 = 80%  (과열 단계2 감량)
///   5 = 5%   (과열 단계3 감량)
///   0        (과열 단계4 → 전량 청산)
/// </summary>
public class TqqqWeightBacktester
{
    private readonly IDataFeedServiceFactory _dataFeedFactory;
    private readonly IIndicatorService       _indicators;
    private readonly ILogger<TqqqWeightBacktester> _logger;

    public TqqqWeightBacktester(
        IDataFeedServiceFactory dataFeedFactory,
        IIndicatorService indicators,
        ILogger<TqqqWeightBacktester> logger)
    {
        _dataFeedFactory = dataFeedFactory;
        _indicators      = indicators;
        _logger          = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Public entry point
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<TqqqBacktestResult> RunAsync(
        DateTime from,
        DateTime to,
        decimal  initialCapital,
        DataSource? dataSource,
        TqqqStrategyParams? strategyParams = null,
        CancellationToken ct = default)
    {
        var p = strategyParams ?? new TqqqStrategyParams();

        _logger.LogInformation(
            "TQQQ 비중 백테스트 시작: {From:d} ~ {To:d}, 초기자본={Capital:N0}",
            from, to, initialCapital);

        // 1. 데이터 피드 선택
        var feed = dataSource.HasValue
            ? _dataFeedFactory.GetService(dataSource.Value)
            : await _dataFeedFactory.GetServiceAsync(ct);

        // 2. TQQQ, QQQ, SPY 일봉 데이터 동시 조회
        //    from을 충분히 앞으로 당겨 SMA200 워밍업 구간 확보 (예비 300일)
        var warmupFrom = from.AddDays(-300);

        var (tqqqBars, qqqBars, spyBars) = await FetchAllBarsAsync(
            feed, warmupFrom, to, ct);

        if (tqqqBars.Count < 200 || qqqBars.Count < 200 || spyBars.Count < 200)
        {
            _logger.LogWarning(
                "데이터 부족: TQQQ={T}, QQQ={Q}, SPY={S} (최소 200일 필요)",
                tqqqBars.Count, qqqBars.Count, spyBars.Count);
            return new TqqqBacktestResult();
        }

        // 3. 날짜 공통 교집합 (세 심볼이 모두 거래된 날만)
        var (dates, tqqqC, qqqC, spyC) = AlignByDate(tqqqBars, qqqBars, spyBars);
        int n = dates.Length;

        // 4. 지표 계산
        var tqqqMa200 = _indicators.SMA(tqqqC, 200);
        var qqqMa3    = _indicators.SMA(qqqC,  3);
        var qqqMa161  = _indicators.SMA(qqqC,  161);
        var spyMa200  = _indicators.SMA(spyC,  200);

        // 5. 보조 지표 (자체 구현 필요한 것들)
        var qqqRsi    = RsiWilder(qqqC, p.RsiLen);
        var tqqqVol20 = SampleStdev(DailyReturns(tqqqC), p.VolLen);
        var qqqSlope  = RollingLinregSlope(qqqMa161, p.SlopeLen);

        // 6. 비중 계산 (Python compute_basic_strategy 번역)
        var (codes, weights) = ComputeWeights(
            qqqC, qqqMa3, qqqMa161, qqqRsi,
            tqqqC, tqqqMa200, tqqqVol20, qqqSlope,
            spyC, spyMa200,
            dates, from, p);

        // 7. 수익률 집계 (백테스트 기간 내 날짜만 대상)
        return BuildResult(dates, tqqqC, codes, weights, initialCapital, from);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  데이터 조회 헬퍼
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<(List<OhlcvBar>, List<OhlcvBar>, List<OhlcvBar>)> FetchAllBarsAsync(
        IDataFeedService feed, DateTime from, DateTime to, CancellationToken ct)
    {
        var tqqqTask = feed.GetHistoricalBarsAsync("TQQQ", TimeFrame.Daily, from, to, ct);
        var qqqTask  = feed.GetHistoricalBarsAsync("QQQ",  TimeFrame.Daily, from, to, ct);
        var spyTask  = feed.GetHistoricalBarsAsync("SPY",  TimeFrame.Daily, from, to, ct);

        await Task.WhenAll(tqqqTask, qqqTask, spyTask);
        return (tqqqTask.Result, qqqTask.Result, spyTask.Result);
    }

    /// <summary>세 심볼의 OHLCV 바를 날짜 기준으로 교집합 정렬.</summary>
    private static (DateTime[] dates, decimal[] tqqqC, decimal[] qqqC, decimal[] spyC)
        AlignByDate(List<OhlcvBar> tqqq, List<OhlcvBar> qqq, List<OhlcvBar> spy)
    {
        var tqqqMap = tqqq.ToDictionary(b => b.Timestamp.Date, b => b.Close);
        var qqqMap  = qqq .ToDictionary(b => b.Timestamp.Date, b => b.Close);
        var spyMap  = spy .ToDictionary(b => b.Timestamp.Date, b => b.Close);

        var commonDates = tqqqMap.Keys
            .Where(d => qqqMap.ContainsKey(d) && spyMap.ContainsKey(d))
            .OrderBy(d => d)
            .ToArray();

        var tqqqC = commonDates.Select(d => tqqqMap[d]).ToArray();
        var qqqC  = commonDates.Select(d => qqqMap[d] ).ToArray();
        var spyC  = commonDates.Select(d => spyMap[d] ).ToArray();

        return (commonDates, tqqqC, qqqC, spyC);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  핵심 비중 계산 — Python compute_basic_strategy 1:1 번역
    // ─────────────────────────────────────────────────────────────────────────

    private static (int[] codes, decimal[] weights) ComputeWeights(
        decimal[] qqqClose,   decimal[] qqqMa3,  decimal[] qqqMa161, decimal[] qqqRsi,
        decimal[] tqqqClose,  decimal[] tqqqMa200,
        decimal[] tqqqVol20,
        decimal[] qqqSlope,
        decimal[] spyClose,   decimal[] spyMa200,
        DateTime[] dates,
        DateTime backtestFrom,
        TqqqStrategyParams p)
    {
        int n = tqqqClose.Length;
        int[] codes   = new int[n];
        decimal[] weights = new decimal[n];

        // ── 상태 변수 ─────────────────────────────────────────────────────────
        bool    locked          = false;    // 변동성 락
        int     assetCode       = 0;        // 현재 비중 코드
        bool    above200Hyst    = false;    // TQQQ 200일선 히스테리시스 상태
        int     overheatStage   = 0;        // 과열 단계 (0~4)
        decimal fullEntryClose  = 0m;       // 100% 진입 시점 TQQQ 종가 (원금보호 손절 기준)
        bool    reentryLock     = false;    // 손절 후 재진입 락
        bool    spyBull         = false;    // SPY 강세 여부
        int     spyConfirmCnt   = 0;        // SPY 강세 확인 카운트
        bool    tp10CycleActive = false;    // TP10 사이클 진행 중
        bool    tp10Reduced     = false;    // TP10 감량 적용됨
        decimal tp10EntryClose  = 0m;       // TP10 기준 TQQQ 종가

        for (int i = 0; i < n; i++)
        {
            decimal tq    = tqqqClose[i];
            decimal tqMa  = tqqqMa200[i];
            decimal vol   = tqqqVol20[i];
            decimal spyC  = spyClose[i];
            decimal spyMa = spyMa200[i];
            decimal qRsi  = qqqRsi[i];

            // ── (1) 변동성 락 ────────────────────────────────────────────────
            // 20일 vol >= threshold 이면 locked. locked 상태에서는 코드 2 불가.
            // Python: if vol >= vol_thr: locked = True  (해제 로직 없음 — 단순 플래그)
            if (vol >= p.VolThreshold)
                locked = true;
            else
                locked = false;

            // ── (2) SPY 히스테리시스 ─────────────────────────────────────────
            if (p.UseSpyFilter && spyMa > 0)
            {
                decimal spyRatio = spyC / spyMa * 100m;

                if (!spyBull)
                {
                    // 진입 조건: ratio > SpyEnter 이면 카운터 증가
                    if (spyRatio > p.SpyEnter)
                    {
                        spyConfirmCnt++;
                        if (spyConfirmCnt >= p.SpyConfirmDays)
                        {
                            spyBull       = true;
                            spyConfirmCnt = 0;
                        }
                    }
                    else
                    {
                        spyConfirmCnt = 0;
                    }
                }
                else
                {
                    // 이탈 조건: ratio < SpyExit 이면 즉시 약세
                    if (spyRatio < p.SpyExit)
                    {
                        spyBull       = false;
                        spyConfirmCnt = 0;
                    }
                }
            }
            else if (!p.UseSpyFilter)
            {
                spyBull = true; // 필터 비활성화 시 항상 강세로 취급
            }

            // ── (3) 재진입 블록 ──────────────────────────────────────────────
            // 손절 후 재진입 락: QQQ RSI >= rsi_reentry_thr 이면 해제
            if (reentryLock && qRsi >= p.RsiReentryThr)
                reentryLock = false;

            // ── (4) TQQQ 200일선 히스테리시스 ───────────────────────────────
            if (tqMa > 0)
            {
                decimal dist = tq / tqMa * 100m;

                if (!above200Hyst)
                {
                    if (dist >= p.Dist200Enter)
                        above200Hyst = true;
                }
                else
                {
                    if (dist < p.Dist200Exit)
                        above200Hyst = false;
                }
            }

            // ── (5) 최상위 우선순위 체크 (Python: 1순위) ────────────────────
            // Python: if locked or spyForceCash or reentryBlockedNow: baseCode = 0
            // 변동성 락 / SPY 강제 현금 / 재진입 블록 → 무조건 0% (현금)
            bool spyForceCash = !spyBull && p.UseSpyFilter && p.SpyBearCap <= 0m;
            int code;

            if (locked || spyForceCash || reentryLock)
            {
                code = 0;
            }
            else if (above200Hyst)
            {
                code = 2; // 200일선 위 + SPY 강세 + 재진입 락 없음 → 100%
            }
            else
            {
                // 200일선 아래, SPY 강세, 재진입 락 없음 → QQQ MA 기반 소규모 포지션
                code = (qqqMa3[i] > qqqMa161[i]) ? 1 : 0;
            }

            // ── (6) 기울기 부스트 ────────────────────────────────────────────
            // code==1(10%)인데 기울기 조건 충족 시 code=2(100%)로 상향
            // locked일 때는 이미 code=0이므로 진입 불가
            if (p.UseSlopeBoost && code == 1 && tqMa > 0 && !locked)
            {
                decimal slope  = qqqSlope[i];
                decimal dist   = tq / tqMa * 100m;

                if (slope >= p.SlopeThr && dist <= p.DistCap && vol <= p.VolCap)
                    code = 2;
            }

            // ── (7) 과열 단계 히스테리시스 ──────────────────────────────────
            // 과열 단계는 200일선 대비 비율로 판단
            if (p.UseOverheatSplit && code == 2 && tqMa > 0)
            {
                decimal dist = tq / tqMa * 100m;

                switch (overheatStage)
                {
                    case 0:
                        if      (dist >= p.Overheat4Enter) overheatStage = 4;
                        else if (dist >= p.Overheat3Enter) overheatStage = 3;
                        else if (dist >= p.Overheat2Enter) overheatStage = 2;
                        else if (dist >= p.Overheat1Enter) overheatStage = 1;
                        break;

                    case 1:
                        if      (dist >= p.Overheat4Enter) overheatStage = 4;
                        else if (dist >= p.Overheat3Enter) overheatStage = 3;
                        else if (dist >= p.Overheat2Enter) overheatStage = 2;
                        else if (dist <  p.Overheat1Exit)  overheatStage = 0;
                        break;

                    case 2:
                        if      (dist >= p.Overheat4Enter) overheatStage = 4;
                        else if (dist >= p.Overheat3Enter) overheatStage = 3;
                        else if (dist <  p.Overheat2Exit)  overheatStage = 1;
                        break;

                    case 3:
                        if      (dist >= p.Overheat4Enter) overheatStage = 4;
                        else if (dist <  p.Overheat3Exit)  overheatStage = 2;
                        break;

                    case 4:
                        if (dist < p.Overheat4Exit) overheatStage = 3;
                        break;
                }
            }
            else if (code != 2)
            {
                // 200일선 아래로 내려오면 과열 단계 초기화
                overheatStage = 0;
            }

            // ── (8) 과열 감량 적용 ───────────────────────────────────────────
            if (p.UseOverheatSplit && code == 2)
            {
                code = overheatStage switch
                {
                    4 => 0,   // 완전 청산
                    3 => 5,   // 5%
                    2 => 4,   // 80%
                    1 => 3,   // 90%
                    _ => 2    // 100%
                };
            }

            // ── (9) 원금보호 손절 ─────────────────────────────────────────────
            // 고비중 진입 후 close가 진입가 × PrincipalStopPct 이하로 하락 시 전량 청산
            if (p.UsePrincipalStop)
            {
                // 비중 기반 판단 (코드 2/3/4/5 + TP10 인코딩(>5) 모두 포함)
                decimal prevWeight = CodeWeight(assetCode);
                bool isHighWeight = prevWeight > 0.10m;  // 10% 초과면 고비중
                bool wasLow       = prevWeight <= 0.10m; // 10% 이하면 저비중

                decimal curWeight = CodeWeight(code);
                if (wasLow && curWeight > 0.10m)
                {
                    // 새로 고비중 진입 → 진입가 기록
                    fullEntryClose  = tq;
                    tp10CycleActive = false;
                    tp10Reduced     = false;
                    tp10EntryClose  = 0m;
                }

                if (isHighWeight && fullEntryClose > 0 && tq <= fullEntryClose * p.PrincipalStopPct)
                {
                    // 원금보호 손절 발동
                    code        = 0;
                    reentryLock = true;
                    fullEntryClose = 0m;
                }
            }

            // ── (10) SPY 약세 캡 ─────────────────────────────────────────────
            // spyBull==false 이고 SpyBearCap 설정된 경우 비중 상한 적용
            if (!spyBull && p.UseSpyFilter)
            {
                decimal capWeight = p.SpyBearCap;
                decimal curWeight = CodeWeight(code);
                if (curWeight > capWeight)
                    code = DecodeWeight(capWeight);
            }

            // ── (11) TP10 사이클 ──────────────────────────────────────────────
            // 100%(code==2, 과열 없음) 진입 후 +10% 수익 달성 시 Tp10Cap으로 감량 유지
            if (p.UseTp10 && code == 2)
            {
                if (!tp10CycleActive)
                {
                    // 새 100% 진입
                    tp10CycleActive = true;
                    tp10Reduced     = false;
                    tp10EntryClose  = tq;
                }
                else if (!tp10Reduced && tp10EntryClose > 0)
                {
                    decimal gain = (tq - tp10EntryClose) / tp10EntryClose;
                    if (gain >= p.Tp10Trigger)
                    {
                        // +10% 달성 → Tp10Cap으로 감량 (code를 1000단위로 인코딩)
                        code        = EncodeWeight(p.Tp10Cap);
                        tp10Reduced = true;
                    }
                }
                else if (tp10Reduced)
                {
                    // 이미 감량됨 → 감량 비중 유지 (Python: elif tp10_reduced: code = encode_weight(tp10_cap))
                    code = EncodeWeight(p.Tp10Cap);
                }
            }
            else if (code != 2)
            {
                tp10CycleActive = false;
                tp10Reduced     = false;
                tp10EntryClose  = 0m;
            }

            // ── 상태 업데이트 및 기록 ─────────────────────────────────────────
            assetCode  = code;
            codes[i]   = code;
            weights[i] = CodeWeight(code);
        }

        return (codes, weights);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  결과 집계
    // ─────────────────────────────────────────────────────────────────────────

    private static TqqqBacktestResult BuildResult(
        DateTime[] dates,
        decimal[]  tqqqClose,
        int[]      codes,
        decimal[]  weights,
        decimal    initialCapital,
        DateTime   backtestFrom)
    {
        var records     = new List<TqqqDailyRecord>();
        decimal equity  = initialCapital;
        decimal peak    = initialCapital;
        decimal maxDD   = 0m;
        int     investedDays = 0;

        // 누적 수익률 계산을 위한 분자
        // portfolio_return[i] = weight[i] * (tqqq[i] / tqqq[i-1] - 1)

        int startIdx = 0;
        // 백테스트 시작일부터만 계산
        for (int i = 0; i < dates.Length; i++)
        {
            if (dates[i] >= backtestFrom.Date)
            {
                startIdx = i;
                break;
            }
        }

        // 첫 번째 날은 전일 대비 수익률을 계산할 수 없으므로 i > startIdx 부터
        // 핵심: 오늘 수익률에는 **어제 결정한 비중**(weights[i-1])을 적용해야 함.
        // weights[i]는 오늘 종가를 보고 결정한 비중이므로, 오늘 수익률에 적용하면
        // look-ahead bias (미래 참조 버그) — 특히 손절/리밸런싱 시 하락분을 안 먹는 문제 발생.
        for (int i = startIdx; i < dates.Length; i++)
        {
            decimal dailyReturn = 0m;

            if (i > 0 && tqqqClose[i - 1] > 0)
            {
                decimal tqqqRet = tqqqClose[i] / tqqqClose[i - 1] - 1m;
                // 어제의 비중(weights[i-1])으로 오늘 수익률 계산
                decimal prevWeight = (i - 1 >= 0) ? weights[i - 1] : 0m;
                dailyReturn     = prevWeight * tqqqRet;
            }

            equity *= (1m + dailyReturn);

            if (equity > peak) peak = equity;
            decimal dd = peak > 0 ? (peak - equity) / peak : 0m;
            if (dd > maxDD) maxDD = dd;

            if (weights[i] > 0) investedDays++;

            decimal cumReturn = initialCapital > 0 ? (equity - initialCapital) / initialCapital : 0m;

            records.Add(new TqqqDailyRecord
            {
                Date             = dates[i],
                Code             = codes[i],
                Weight           = weights[i],
                TqqqClose        = tqqqClose[i],
                DailyReturn      = dailyReturn,
                CumulativeReturn = cumReturn,
                Equity           = equity
            });
        }

        int totalDays = records.Count;
        decimal totalReturn = records.Count > 0
            ? (records[^1].Equity - initialCapital) / initialCapital
            : 0m;

        // CAGR: (finalEquity / initialCapital)^(1/years) - 1
        decimal cagr = 0m;
        if (totalDays > 0 && initialCapital > 0)
        {
            double years = totalDays / 252.0;
            if (years > 0)
            {
                double ratio = (double)(records[^1].Equity / initialCapital);
                if (ratio > 0)
                    cagr = (decimal)(Math.Pow(ratio, 1.0 / years) - 1.0);
            }
        }

        // Sharpe: annualized mean / annualized std of daily portfolio returns
        decimal sharpe = ComputeSharpe(records);

        return new TqqqBacktestResult
        {
            TotalReturnPercent  = totalReturn * 100m,
            Cagr                = cagr * 100m,
            MaxDrawdownPercent  = maxDD * 100m,
            SharpeRatio         = sharpe,
            TotalDays           = totalDays,
            InvestedDays        = investedDays,
            DailyRecords        = records
        };
    }

    private static decimal ComputeSharpe(List<TqqqDailyRecord> records)
    {
        if (records.Count < 2) return 0m;

        decimal sum  = 0m;
        decimal sum2 = 0m;
        int     cnt  = 0;

        foreach (var r in records)
        {
            sum  += r.DailyReturn;
            sum2 += r.DailyReturn * r.DailyReturn;
            cnt++;
        }

        if (cnt < 2) return 0m;

        decimal mean = sum / cnt;
        decimal var_ = sum2 / cnt - mean * mean;
        if (var_ <= 0m) return 0m;

        decimal std  = (decimal)Math.Sqrt((double)var_);
        decimal annualizedMean  = mean  * 252m;
        decimal annualizedStd   = std   * (decimal)Math.Sqrt(252.0);

        return annualizedStd > 0 ? annualizedMean / annualizedStd : 0m;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  보조 지표 계산
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wilder RSI (Python rsi_wilder 번역).
    /// SMA로 seed 후 Wilder 스무딩 (alpha = 1/length).
    /// </summary>
    private static decimal[] RsiWilder(decimal[] close, int length)
    {
        int n      = close.Length;
        var result = new decimal[n];
        if (n <= length) return result;

        // Seed: 첫 length 기간 SMA 방식으로 avgGain/avgLoss 계산
        decimal avgGain = 0m, avgLoss = 0m;
        for (int i = 1; i <= length; i++)
        {
            decimal diff = close[i] - close[i - 1];
            if (diff > 0) avgGain += diff;
            else          avgLoss += Math.Abs(diff);
        }
        avgGain /= length;
        avgLoss /= length;

        result[length] = avgLoss == 0m ? 100m : 100m - (100m / (1m + avgGain / avgLoss));

        // Wilder 스무딩: avgGain = (avgGain*(length-1) + gain) / length
        for (int i = length + 1; i < n; i++)
        {
            decimal diff = close[i] - close[i - 1];
            decimal gain = diff > 0 ? diff         : 0m;
            decimal loss = diff < 0 ? Math.Abs(diff) : 0m;

            avgGain = (avgGain * (length - 1) + gain) / length;
            avgLoss = (avgLoss * (length - 1) + loss) / length;

            result[i] = avgLoss == 0m ? 100m : 100m - (100m / (1m + avgGain / avgLoss));
        }
        return result;
    }

    /// <summary>
    /// 일별 수익률 배열 계산.
    /// returns[0] = 0 (기준일), returns[i] = close[i]/close[i-1] - 1.
    /// </summary>
    private static decimal[] DailyReturns(decimal[] close)
    {
        int n      = close.Length;
        var result = new decimal[n];
        for (int i = 1; i < n; i++)
        {
            if (close[i - 1] != 0)
                result[i] = close[i] / close[i - 1] - 1m;
        }
        return result;
    }

    /// <summary>
    /// 표본표준편차 롤링 (Python sample_stdev 번역).
    /// 일별 수익률 배열에 대해 length 구간 표본 std 계산.
    /// </summary>
    private static decimal[] SampleStdev(decimal[] returns, int length)
    {
        int n      = returns.Length;
        var result = new decimal[n];
        if (n < length) return result;

        // 슬라이딩 윈도우로 E[X]와 E[X^2] 유지
        decimal sumX  = 0m;
        decimal sumX2 = 0m;

        // 초기 윈도우 시드
        for (int i = 0; i < length - 1 && i < n; i++)
        {
            sumX  += returns[i];
            sumX2 += returns[i] * returns[i];
        }

        for (int i = length - 1; i < n; i++)
        {
            sumX  += returns[i];
            sumX2 += returns[i] * returns[i];

            // 표본 분산: (sumX2 - sumX^2/length) / (length-1)
            decimal mean   = sumX / length;
            decimal varSam = (sumX2 - sumX * sumX / length) / (length - 1);
            if (varSam < 0m) varSam = 0m;

            result[i] = (decimal)Math.Sqrt((double)varSam);

            // 슬라이드
            int outIdx = i - length + 1;
            sumX  -= returns[outIdx];
            sumX2 -= returns[outIdx] * returns[outIdx];
        }
        return result;
    }

    /// <summary>
    /// 롤링 선형 회귀 기울기 (Python rolling_linreg_slope 번역).
    /// y[i - length + 1 .. i]에 대해 OLS slope 계산.
    /// 정규화: slope / mean(y_window) × 100 (% per day 단위로 반환).
    /// </summary>
    private static decimal[] RollingLinregSlope(decimal[] y, int length)
    {
        int n      = y.Length;
        var result = new decimal[n];
        if (n < length) return result;

        // x = 0, 1, ..., length-1
        // x̄ = (length-1)/2
        decimal xMean     = (length - 1) / 2.0m;
        decimal xVar      = 0m;
        for (int k = 0; k < length; k++)
            xVar += (k - xMean) * (k - xMean);
        // xVar = Σ(xi - x̄)^2

        for (int i = length - 1; i < n; i++)
        {
            int start = i - length + 1;

            decimal yMean = 0m;
            for (int k = 0; k < length; k++)
                yMean += y[start + k];
            yMean /= length;

            decimal cov = 0m;
            for (int k = 0; k < length; k++)
                cov += (k - xMean) * (y[start + k] - yMean);

            decimal slope = xVar > 0 ? cov / xVar : 0m;

            // 정규화: slope / yMean × 100 (yMean이 0이면 0)
            result[i] = yMean != 0m ? slope / yMean * 100m : 0m;
        }
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  비중 코드 ↔ 비율 변환
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>비중 코드를 실제 비중(0~1)으로 변환.</summary>
    internal static decimal CodeWeight(int code) => code switch
    {
        0 => 0.00m,
        1 => 0.10m,
        2 => 1.00m,
        3 => 0.90m,
        4 => 0.80m,
        5 => 0.05m,
        _ => code > 5 ? Math.Min(Math.Max(code / 1000m, 0m), 1m) : 0m
    };

    /// <summary>비중(0~1)을 가장 가까운 표준 코드로 인코딩.</summary>
    internal static int DecodeWeight(decimal weight)
    {
        if (weight <= 0m)   return 0;
        if (weight >= 1m)   return 2;
        if (weight >= 0.95m) return EncodeWeight(weight); // 중간 단계 → 1000 인코딩
        if (weight >= 0.85m) return 3; // 90%
        if (weight >= 0.75m) return 4; // 80%
        if (weight >= 0.075m) return 5; // 5%
        return 1; // 10%
    }

    /// <summary>비중을 1000단위 코드로 인코딩 (TP10 감량용).</summary>
    internal static int EncodeWeight(decimal weight) =>
        (int)Math.Round(Math.Min(Math.Max(weight, 0m), 1m) * 1000m);
}

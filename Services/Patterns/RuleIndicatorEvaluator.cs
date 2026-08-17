using StockTrader.Domain.MarketData;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

/// <summary>
/// 서버 카탈로그에 등록된 기술지표를 계산하고 한 평가 주기 안에서 결과를 캐시한다.
/// 규칙 결합과 진입·청산 정책은 소유하지 않는다.
/// </summary>
internal sealed class RuleIndicatorEvaluator
{
    private readonly IIndicatorService _indicators;

    public RuleIndicatorEvaluator(IIndicatorService indicators)
    {
        _indicators = indicators;
    }

    internal EvalContext CreateContext(OhlcvBar[] bars) => new(bars, _indicators);

    /// <summary>
    /// offset=0 → 현재 봉, offset=1 → 1봉 전, ...
    /// 반환: (해당 봉의 값, 그 이전 봉의 값) — crosses 연산자용
    /// </summary>
    internal (decimal current, decimal prev) Compute(
        string indicator, Dictionary<string, decimal> prms, EvalContext ctx, int offset)
    {
        int GetInt(string key, int def) =>
            prms.TryGetValue(key, out var v) ? (int)v : (int)IndicatorCatalog.ParameterDefault(indicator, key, def);
        decimal GetDec(string key, decimal def) =>
            prms.TryGetValue(key, out var v) ? v : IndicatorCatalog.ParameterDefault(indicator, key, def);

        var bars = ctx.Bars;
        var closes = ctx.Closes;
        // 인덱스: 현재 봉은 ^(1+offset), 이전 봉은 ^(2+offset)
        int ci = bars.Length - 1 - offset;
        int pi = bars.Length - 2 - offset;
        if (ci < 2 || pi < 1) return (0, 0);

        switch (indicator.ToUpperInvariant())
        {
            // ═══════════════════════════════════════════════════════════
            // 기본 지표
            // ═══════════════════════════════════════════════════════════

            case "RSI":
            {
                var period = GetInt("period", 14);
                var rsi = ctx.GetRsi(period);
                return (rsi[ci], rsi[pi]);
            }

            case "CUMULATIVE_RSI":
            {
                var period = GetInt("period", 2);
                var cumulativePeriod = GetInt("cumulativePeriod", 2);
                var cumulativeRsi = _indicators.CumulativeRsi(closes, period, cumulativePeriod);
                return (cumulativeRsi[ci], cumulativeRsi[pi]);
            }

            case "PRICE_VS_SMA":
            {
                var period = GetInt("period", 200);
                var sma = ctx.GetSma(period);
                if (sma[ci] == 0) return (0, 0);
                return ((closes[ci] - sma[ci]) / sma[ci] * 100,
                        sma[pi] == 0 ? 0 : (closes[pi] - sma[pi]) / sma[pi] * 100);
            }

            case "PRICE_VS_EMA":
            {
                var period = GetInt("period", 20);
                var ema = ctx.GetEma(period);
                if (ema[ci] == 0) return (0, 0);
                return ((closes[ci] - ema[ci]) / ema[ci] * 100,
                        ema[pi] == 0 ? 0 : (closes[pi] - ema[pi]) / ema[pi] * 100);
            }

            case "MACD_HIST":
            {
                var fast = GetInt("fast", 12);
                var slow = GetInt("slow", 26);
                var sig = GetInt("signal", 9);
                var (_, _, hist) = ctx.GetMacd(fast, slow, sig);
                return (hist[ci], hist[pi]);
            }

            case "BOLLINGER_POS":
            {
                var period = GetInt("period", 20);
                var stddev = GetDec("stddev", 2.0m);
                var (upper, _, lower) = ctx.GetBollinger(period, stddev);
                var range = upper[ci] - lower[ci];
                if (range == 0) return (0.5m, 0.5m);
                var curr = (closes[ci] - lower[ci]) / range;
                var prevRange = upper[pi] - lower[pi];
                var prev = prevRange == 0 ? 0.5m : (closes[pi] - lower[pi]) / prevRange;
                return (curr, prev);
            }

            case "VOLUME_RATIO":
            {
                var period = GetInt("period", 20);
                if (ci < period) return (0, 0);
                decimal avgVol = 0;
                for (int i = ci - period; i < ci; i++) avgVol += bars[i].Volume;
                avgVol /= period;
                if (avgVol == 0) return (0, 0);
                var curr = bars[ci].Volume / avgVol;
                decimal prevAvg = 0;
                if (pi >= period)
                {
                    for (int i = pi - period; i < pi; i++) prevAvg += bars[i].Volume;
                    prevAvg /= period;
                }
                var prev = prevAvg == 0 ? 0 : bars[pi].Volume / prevAvg;
                return (curr, prev);
            }

            case "PRICE_CHANGE":
            {
                var barsBack = GetInt("bars", 1);
                if (ci < barsBack || pi < barsBack) return (0, 0);
                var refC = closes[ci - barsBack];
                var refP = closes[pi - barsBack];
                return (refC == 0 ? 0 : (closes[ci] - refC) / refC * 100,
                        refP == 0 ? 0 : (closes[pi] - refP) / refP * 100);
            }

            case "ATR":
            {
                var period = GetInt("period", 14);
                var atr = ctx.GetAtr(period);
                return (atr[ci], atr[pi]);
            }

            case "SMA_SLOPE":
            {
                var period = GetInt("period", 20);
                var lookback = GetInt("lookback", 5);
                var sma = ctx.GetSma(period);
                if (ci < lookback || sma[ci - lookback] == 0) return (0, 0);
                var curr = (sma[ci] - sma[ci - lookback]) / sma[ci - lookback] * 100;
                var prev = (pi < lookback || sma[pi - lookback] == 0) ? 0
                    : (sma[pi] - sma[pi - lookback]) / sma[pi - lookback] * 100;
                return (curr, prev);
            }

            case "CANDLE_BODY":
            {
                static decimal Body(OhlcvBar b)
                {
                    var range = b.High - b.Low;
                    return range == 0 ? 0 : Math.Abs(b.Close - b.Open) / range;
                }
                return (Body(bars[ci]), Body(bars[pi]));
            }

            // ═══════════════════════════════════════════════════════════
            // 가격 구조
            // ═══════════════════════════════════════════════════════════

            case "DIST_FROM_HIGH":
            {
                // N봉 고점에서 현재가까지 내려온 거리 (%). 0에 가까울수록 고점 부근
                var lookback = GetInt("period", 52);
                var start = Math.Max(0, ci - lookback + 1);
                decimal high = 0;
                for (int i = start; i <= ci; i++) if (bars[i].High > high) high = bars[i].High;
                if (high == 0) return (0, 0);
                var curr = (high - closes[ci]) / high * 100;
                decimal prevHigh = 0;
                var pStart = Math.Max(0, pi - lookback + 1);
                for (int i = pStart; i <= pi; i++) if (bars[i].High > prevHigh) prevHigh = bars[i].High;
                var prev = prevHigh == 0 ? 0 : (prevHigh - closes[pi]) / prevHigh * 100;
                return (curr, prev);
            }

            case "DIST_FROM_LOW":
            {
                // N일 저점 대비 현재가 거리 (%). 양수 = 저점 대비 상승
                var lookback = GetInt("period", 52);
                var start = Math.Max(0, ci - lookback + 1);
                decimal low = decimal.MaxValue;
                for (int i = start; i <= ci; i++) if (bars[i].Low < low) low = bars[i].Low;
                if (low == decimal.MaxValue || low == 0) return (0, 0);
                var curr = (closes[ci] - low) / low * 100;
                decimal prevLow = decimal.MaxValue;
                var pStart = Math.Max(0, pi - lookback + 1);
                for (int i = pStart; i <= pi; i++) if (bars[i].Low < prevLow) prevLow = bars[i].Low;
                var prev = (prevLow == decimal.MaxValue || prevLow == 0) ? 0
                    : (closes[pi] - prevLow) / prevLow * 100;
                return (curr, prev);
            }

            case "BREAKOUT_HIGH":
            {
                // N일 신고가 돌파: 1 = 돌파, 0 = 아님
                var lookback = GetInt("period", 20);
                var start = Math.Max(0, ci - lookback);
                decimal prevHigh = 0;
                for (int i = start; i < ci; i++) if (bars[i].High > prevHigh) prevHigh = bars[i].High;
                var curr = closes[ci] > prevHigh ? 1m : 0m;
                decimal ppHigh = 0;
                var pStart = Math.Max(0, pi - lookback);
                for (int i = pStart; i < pi; i++) if (bars[i].High > ppHigh) ppHigh = bars[i].High;
                var prev = closes[pi] > ppHigh ? 1m : 0m;
                return (curr, prev);
            }

            case "BREAKOUT_LOW":
            {
                // N일 신저가 돌파: 1 = 돌파, 0 = 아님
                var lookback = GetInt("period", 20);
                var start = Math.Max(0, ci - lookback);
                decimal prevLow = decimal.MaxValue;
                for (int i = start; i < ci; i++) if (bars[i].Low < prevLow) prevLow = bars[i].Low;
                var curr = closes[ci] < prevLow ? 1m : 0m;
                decimal ppLow = decimal.MaxValue;
                var pStart = Math.Max(0, pi - lookback);
                for (int i = pStart; i < pi; i++) if (bars[i].Low < ppLow) ppLow = bars[i].Low;
                var prev = closes[pi] < ppLow ? 1m : 0m;
                return (curr, prev);
            }

            case "GAP":
            {
                // 갭 비율 (%): (Open - PrevClose) / PrevClose * 100
                if (ci < 1) return (0, 0);
                var curr = closes[ci - 1] == 0 ? 0
                    : (bars[ci].Open - closes[ci - 1]) / closes[ci - 1] * 100;
                var prev = (pi < 1 || closes[pi - 1] == 0) ? 0
                    : (bars[pi].Open - closes[pi - 1]) / closes[pi - 1] * 100;
                return (curr, prev);
            }

            case "HIGHER_LOW":
            {
                // 연속 Higher Low 봉 수
                int count = 0;
                for (int i = ci; i > 0 && i > ci - 20; i--)
                {
                    if (bars[i].Low > bars[i - 1].Low) count++;
                    else break;
                }
                int prevCount = 0;
                for (int i = pi; i > 0 && i > pi - 20; i--)
                {
                    if (bars[i].Low > bars[i - 1].Low) prevCount++;
                    else break;
                }
                return (count, prevCount);
            }

            case "LOWER_HIGH":
            {
                // 연속 Lower High 봉 수
                int count = 0;
                for (int i = ci; i > 0 && i > ci - 20; i--)
                {
                    if (bars[i].High < bars[i - 1].High) count++;
                    else break;
                }
                int prevCount = 0;
                for (int i = pi; i > 0 && i > pi - 20; i--)
                {
                    if (bars[i].High < bars[i - 1].High) prevCount++;
                    else break;
                }
                return (count, prevCount);
            }

            case "INSIDE_BAR":
            {
                // 인사이드바: 1 = 현재 봉이 이전 봉 범위 내, 0 = 아님
                if (ci < 1) return (0, 0);
                var curr = (bars[ci].High <= bars[ci - 1].High && bars[ci].Low >= bars[ci - 1].Low) ? 1m : 0m;
                var prev = (pi >= 1 && bars[pi].High <= bars[pi - 1].High && bars[pi].Low >= bars[pi - 1].Low) ? 1m : 0m;
                return (curr, prev);
            }

            case "ENGULFING":
            {
                // 장악형: +1=강세 장악, -1=약세 장악, 0=아님
                if (ci < 1) return (0, 0);
                static decimal CheckEngulfing(OhlcvBar curr, OhlcvBar prev)
                {
                    var currBull = curr.Close > curr.Open;
                    var prevBull = prev.Close > prev.Open;
                    if (currBull && !prevBull && curr.Close > prev.Open && curr.Open < prev.Close) return 1m;
                    if (!currBull && prevBull && curr.Close < prev.Open && curr.Open > prev.Close) return -1m;
                    return 0m;
                }
                return (CheckEngulfing(bars[ci], bars[ci - 1]),
                        pi >= 1 ? CheckEngulfing(bars[pi], bars[pi - 1]) : 0m);
            }

            // ═══════════════════════════════════════════════════════════
            // 모멘텀 / 추세
            // ═══════════════════════════════════════════════════════════

            case "CONSECUTIVE_UP":
            {
                // 연속 양봉 수
                int count = 0;
                for (int i = ci; i > 0 && i > ci - 30; i--)
                {
                    if (closes[i] > closes[i - 1]) count++;
                    else break;
                }
                int prevCount = 0;
                for (int i = pi; i > 0 && i > pi - 30; i--)
                {
                    if (closes[i] > closes[i - 1]) prevCount++;
                    else break;
                }
                return (count, prevCount);
            }

            case "CONSECUTIVE_DOWN":
            {
                // 연속 음봉 수
                int count = 0;
                for (int i = ci; i > 0 && i > ci - 30; i--)
                {
                    if (closes[i] < closes[i - 1]) count++;
                    else break;
                }
                int prevCount = 0;
                for (int i = pi; i > 0 && i > pi - 30; i--)
                {
                    if (closes[i] < closes[i - 1]) prevCount++;
                    else break;
                }
                return (count, prevCount);
            }

            case "ADX":
            {
                // ADX (Average Directional Index) 직접 계산
                var period = GetInt("period", 14);
                var adx = ComputeAdx(bars, period);
                return (ci < adx.Length ? adx[ci] : 0, pi < adx.Length ? adx[pi] : 0);
            }

            case "STOCHASTIC_K":
            {
                var period = GetInt("period", 14);
                var (k, _) = ComputeStochastic(bars, period, 3);
                return (ci < k.Length ? k[ci] : 0, pi < k.Length ? k[pi] : 0);
            }

            case "STOCHASTIC_D":
            {
                var period = GetInt("period", 14);
                var smooth = GetInt("smooth", 3);
                var (_, d) = ComputeStochastic(bars, period, smooth);
                return (ci < d.Length ? d[ci] : 0, pi < d.Length ? d[pi] : 0);
            }

            // ═══════════════════════════════════════════════════════════
            // 복합 지표
            // ═══════════════════════════════════════════════════════════

            case "ATR_PERCENT":
            {
                // ATR / 종가 * 100 (변동성 비율 %)
                var period = GetInt("period", 14);
                var atr = ctx.GetAtr(period);
                var curr = closes[ci] == 0 ? 0 : atr[ci] / closes[ci] * 100;
                var prev = closes[pi] == 0 ? 0 : atr[pi] / closes[pi] * 100;
                return (curr, prev);
            }

            case "VOLATILITY_20D":
            {
                // 현재 시간축의 N봉 역사적 변동성 (해당 시간축 기준 연율화 표준편차)
                var period = GetInt("period", 20);
                if (ci < period) return (0, 0);
                return (CalcVol(closes, ci, period, bars[ci].TimeFrame),
                        CalcVol(closes, pi, period, bars[pi].TimeFrame));
            }

            // ═══════════════════════════════════════════════════════════
            // 거래량 / VWAP 지표
            // ═══════════════════════════════════════════════════════════

            case "OBV":
            {
                var obv = ctx.GetObv();
                return (obv[ci], obv[pi]);
            }

            case "PRICE_VS_VWAP":
            {
                // 인라인 Rolling VWAP 계산 (IIndicatorService.VWAP는 누적 전용이므로 period 기반 직접 계산)
                var period = GetInt("period", 20);
                static decimal CalcRollingVwap(OhlcvBar[] bars, int idx, int period)
                {
                    if (idx < period - 1) return 0;
                    decimal tpvSum = 0;
                    long volSum = 0;
                    for (int i = idx - period + 1; i <= idx; i++)
                    {
                        var tp = (bars[i].High + bars[i].Low + bars[i].Close) / 3m;
                        tpvSum += tp * bars[i].Volume;
                        volSum += bars[i].Volume;
                    }
                    return volSum == 0 ? 0 : tpvSum / volSum;
                }
                var vwapC = CalcRollingVwap(bars, ci, period);
                var vwapP = CalcRollingVwap(bars, pi, period);
                if (vwapC == 0) return (0, 0);
                return ((closes[ci] - vwapC) / vwapC * 100,
                        vwapP == 0 ? 0 : (closes[pi] - vwapP) / vwapP * 100);
            }

            case "OBV_SLOPE":
            {
                var lookback = GetInt("lookback", 5);
                var obv = ctx.GetObv();
                if (ci < lookback || obv[ci - lookback] == 0) return (0, 0);
                var curr = (obv[ci] - obv[ci - lookback]) / Math.Abs(obv[ci - lookback] == 0 ? 1 : obv[ci - lookback]) * 100;
                var prev = (pi < lookback || obv[pi - lookback] == 0) ? 0
                    : (obv[pi] - obv[pi - lookback]) / Math.Abs(obv[pi - lookback] == 0 ? 1 : obv[pi - lookback]) * 100;
                return (curr, prev);
            }

            // ═══════════════════════════════════════════════════════════
            // 추가 기술적 지표 (CCI, Williams %R, ROC, CMF)
            // ═══════════════════════════════════════════════════════════

            case "CCI":
            {
                var period = GetInt("period", 20);
                static decimal CalcCci(OhlcvBar[] bars, int idx, int period)
                {
                    if (idx < period) return 0;
                    var typicals = new decimal[period];
                    for (int i = 0; i < period; i++)
                    {
                        var b = bars[idx - period + 1 + i];
                        typicals[i] = (b.High + b.Low + b.Close) / 3m;
                    }
                    var mean = typicals.Average();
                    var mad = typicals.Average(t => Math.Abs(t - mean));
                    return mad == 0 ? 0 : (typicals[^1] - mean) / (0.015m * mad);
                }
                return (CalcCci(bars, ci, period), CalcCci(bars, pi, period));
            }

            case "ROC":
            {
                var period = GetInt("period", 14);
                var curr = ci >= period && closes[ci - period] != 0
                    ? (closes[ci] - closes[ci - period]) / closes[ci - period] * 100 : 0;
                var prev = pi >= period && closes[pi - period] != 0
                    ? (closes[pi] - closes[pi - period]) / closes[pi - period] * 100 : 0;
                return (curr, prev);
            }

            case "WILLIAMS_R":
            {
                var period = GetInt("period", 14);
                static decimal CalcWr(OhlcvBar[] bars, int idx, int period)
                {
                    if (idx < period - 1) return -50;
                    decimal hh = 0, ll = decimal.MaxValue;
                    for (int i = idx - period + 1; i <= idx; i++)
                    {
                        if (bars[i].High > hh) hh = bars[i].High;
                        if (bars[i].Low < ll) ll = bars[i].Low;
                    }
                    var range = hh - ll;
                    return range == 0 ? -50 : (hh - bars[idx].Close) / range * -100;
                }
                return (CalcWr(bars, ci, period), CalcWr(bars, pi, period));
            }

            case "CMF":
            {
                var period = GetInt("period", 20);
                static decimal CalcCmf(OhlcvBar[] bars, int idx, int period)
                {
                    if (idx < period) return 0;
                    decimal mfvSum = 0, volSum = 0;
                    for (int i = idx - period + 1; i <= idx; i++)
                    {
                        var range = bars[i].High - bars[i].Low;
                        var mfm = range == 0 ? 0 : ((bars[i].Close - bars[i].Low) - (bars[i].High - bars[i].Close)) / range;
                        mfvSum += mfm * bars[i].Volume;
                        volSum += bars[i].Volume;
                    }
                    return volSum == 0 ? 0 : mfvSum / volSum;
                }
                return (CalcCmf(bars, ci, period), CalcCmf(bars, pi, period));
            }

            default:
                return (0, 0);
        }
    }

    // ── 헬퍼 메서드 ──

    private static decimal CalcVol(decimal[] closes, int endIdx, int period, TimeFrame timeFrame)
    {
        if (endIdx < period) return 0;
        var returns = new decimal[period];
        for (int i = 0; i < period; i++)
        {
            var prev = closes[endIdx - period + i];
            var curr = closes[endIdx - period + i + 1];
            returns[i] = prev == 0 ? 0 : (curr - prev) / prev;
        }
        var mean = returns.Average();
        var variance = returns.Average(r => (r - mean) * (r - mean));
        var periodsPerYear = TimeFrameCatalog.AnnualizationPeriods(timeFrame);
        return (decimal)Math.Sqrt((double)variance) * (decimal)Math.Sqrt((double)periodsPerYear) * 100m;
    }

    private static decimal[] ComputeAdx(OhlcvBar[] bars, int period)
    {
        var n = bars.Length;
        var adx = new decimal[n];
        if (n < period + 1) return adx;

        var plusDm = new decimal[n];
        var minusDm = new decimal[n];
        var tr = new decimal[n];

        for (int i = 1; i < n; i++)
        {
            var upMove = bars[i].High - bars[i - 1].High;
            var downMove = bars[i - 1].Low - bars[i].Low;
            plusDm[i] = upMove > downMove && upMove > 0 ? upMove : 0;
            minusDm[i] = downMove > upMove && downMove > 0 ? downMove : 0;
            tr[i] = Math.Max(bars[i].High - bars[i].Low,
                Math.Max(Math.Abs(bars[i].High - bars[i - 1].Close),
                         Math.Abs(bars[i].Low - bars[i - 1].Close)));
        }

        // Smoothed averages
        decimal smoothPlusDm = 0, smoothMinusDm = 0, smoothTr = 0;
        for (int i = 1; i <= period; i++)
        {
            smoothPlusDm += plusDm[i];
            smoothMinusDm += minusDm[i];
            smoothTr += tr[i];
        }

        var dx = new decimal[n];
        for (int i = period; i < n; i++)
        {
            if (i > period)
            {
                smoothPlusDm = smoothPlusDm - smoothPlusDm / period + plusDm[i];
                smoothMinusDm = smoothMinusDm - smoothMinusDm / period + minusDm[i];
                smoothTr = smoothTr - smoothTr / period + tr[i];
            }
            if (smoothTr == 0) continue;
            var plusDi = smoothPlusDm / smoothTr * 100;
            var minusDi = smoothMinusDm / smoothTr * 100;
            var diSum = plusDi + minusDi;
            dx[i] = diSum == 0 ? 0 : Math.Abs(plusDi - minusDi) / diSum * 100;
        }

        // ADX = SMA of DX
        decimal adxSum = 0;
        for (int i = period; i < period * 2 && i < n; i++) adxSum += dx[i];
        if (period * 2 <= n)
            adx[period * 2 - 1] = adxSum / period;
        for (int i = period * 2; i < n; i++)
            adx[i] = (adx[i - 1] * (period - 1) + dx[i]) / period;

        return adx;
    }

    private static (decimal[] k, decimal[] d) ComputeStochastic(OhlcvBar[] bars, int period, int smooth)
    {
        var n = bars.Length;
        var k = new decimal[n];
        var d = new decimal[n];

        for (int i = period - 1; i < n; i++)
        {
            decimal highest = 0, lowest = decimal.MaxValue;
            for (int j = i - period + 1; j <= i; j++)
            {
                if (bars[j].High > highest) highest = bars[j].High;
                if (bars[j].Low < lowest) lowest = bars[j].Low;
            }
            var range = highest - lowest;
            k[i] = range == 0 ? 50 : (bars[i].Close - lowest) / range * 100;
        }

        // %D = SMA of %K
        for (int i = period - 1 + smooth - 1; i < n; i++)
        {
            decimal sum = 0;
            for (int j = i - smooth + 1; j <= i; j++) sum += k[j];
            d[i] = sum / smooth;
        }

        return (k, d);
    }

    /// <summary>지표 계산 결과 캐시</summary>
    internal sealed class EvalContext
    {
        public readonly OhlcvBar[] Bars;
        public readonly decimal[] Closes;
        private readonly IIndicatorService _ind;
        private readonly Dictionary<string, object> _cache = new();

        public EvalContext(OhlcvBar[] bars, IIndicatorService ind)
        {
            Bars = bars;
            _ind = ind;
            Closes = IndicatorService.ExtractCloses(bars);
        }

        public decimal[] GetRsi(int period) =>
            GetOrAdd($"rsi_{period}", () => _ind.RSI(Closes, period));
        public decimal[] GetSma(int period) =>
            GetOrAdd($"sma_{period}", () => _ind.SMA(Closes, period));
        public decimal[] GetEma(int period) =>
            GetOrAdd($"ema_{period}", () => _ind.EMA(Closes, period));
        public decimal[] GetAtr(int period) =>
            GetOrAdd($"atr_{period}", () => _ind.ATR(Bars, period));
        public (decimal[] Upper, decimal[] Middle, decimal[] Lower) GetBollinger(int period, decimal stddev) =>
            GetOrAdd($"bb_{period}_{stddev}", () => _ind.BollingerBands(Closes, period, stddev));
        public (decimal[] MacdLine, decimal[] SignalLine, decimal[] Histogram) GetMacd(int fast, int slow, int sig) =>
            GetOrAdd($"macd_{fast}_{slow}_{sig}", () => _ind.MACD(Closes, fast, slow, sig));
        public decimal[] GetObv() =>
            GetOrAdd("obv", () => _ind.OBV(Bars));

        private T GetOrAdd<T>(string key, Func<T> factory)
        {
            if (_cache.TryGetValue(key, out var cached)) return (T)cached;
            var val = factory();
            _cache[key] = val!;
            return val;
        }
    }
}

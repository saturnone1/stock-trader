using System.Reflection;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Components.Pages;

/// <summary>
/// 백테스트 UI에서 편집 가능한 패턴 파라미터 뷰모델.
/// appsettings.json 기본값으로 초기화되며, ToOverrides()로 서비스 모델로 변환합니다.
/// </summary>
public sealed class PatternParamViewModel
{
    // GapUpPullback
    public decimal GapUp_MinGapPercent { get; set; } = 0.005m;
    public decimal GapUp_MaxPullbackPercent { get; set; } = 0.8m;
    public decimal GapUp_MinVolumeDecimal { get; set; } = 10m;
    public long GapUp_MinVolume => (long)(GapUp_MinVolumeDecimal * 10_000);

    // Breakout
    public int Breakout_LookbackDays { get; set; } = 10;
    public decimal Breakout_MinVolumeMultiplier { get; set; } = 1.0m;
    public decimal Breakout_BreakoutMarginPercent { get; set; } = 0.001m;
    public decimal Breakout_AtrStopMultiplier { get; set; } = 1.5m;
    public decimal Breakout_AtrTargetMultiplier { get; set; } = 4.0m;

    // VwapReversion
    public decimal Vwap_MaxDeviationPercent { get; set; } = 0.01m;
    public decimal Vwap_MinBouncePercent { get; set; } = 0.003m;
    public decimal Vwap_MinBounceFromLowPercent { get; set; } = 0.2m;

    // RsiMeanReversion
    public int Rsi_OversoldThreshold { get; set; } = 30;
    public int Rsi_Period { get; set; } = 14;
    public decimal Rsi_MinVolumeIncreaseMultiplier { get; set; } = 1.0m;
    public decimal Rsi_AtrStopMultiplier { get; set; } = 1.5m;
    public decimal Rsi_AtrTargetMultiplier { get; set; } = 2.0m;

    // TrendPullback
    public int Trend_MaPeriod { get; set; } = 20;
    public decimal Trend_MaxPullbackFromMa { get; set; } = 0.025m;
    public int Trend_TrendConfirmationDays { get; set; } = 7;
    public decimal Trend_AtrStopMultiplier { get; set; } = 1.5m;
    public decimal Trend_AtrTargetMultiplier { get; set; } = 5.0m;

    // OpeningRangeBreakout
    public int Orb_RangeMinutes { get; set; } = 15;
    public decimal Orb_MinRangePercent { get; set; } = 0.005m;

    // VolumeSpikeContinuation
    public decimal VolSpike_VolumeMultiplier { get; set; } = 1.5m;
    public int VolSpike_ContinuationBars { get; set; } = 2;
    public int VolSpike_VolumeAvgPeriod { get; set; } = 20;
    public decimal VolSpike_AtrStopMultiplier { get; set; } = 2.0m;
    public decimal VolSpike_AtrTargetMultiplier { get; set; } = 3.0m;

    // EarningsDrift
    public int Earnings_DriftDays { get; set; } = 5;
    public decimal Earnings_MinSurprisePercent { get; set; } = 0.05m;

    // IndexRegimeFilter
    public int Regime_MaPeriod { get; set; } = 200;
    public string Regime_IndexSymbol { get; set; } = "SPY";

    // VolatilityExpansion
    public int Vola_BollingerPeriod { get; set; } = 20;
    public decimal Vola_StdDevMultiplier { get; set; } = 2.0m;
    public decimal Vola_AtrStopMultiplier { get; set; } = 2.0m;
    public decimal Vola_AtrTargetMultiplier { get; set; } = 2.0m;

    // MomentumReversal
    public int Mom_FastEmaPeriod { get; set; } = 12;
    public int Mom_SlowEmaPeriod { get; set; } = 26;
    public int Mom_MacdSignalPeriod { get; set; } = 9;
    public int Mom_RsiPeriod { get; set; } = 14;
    public int Mom_RsiOversold { get; set; } = 30;
    public int Mom_RsiOverbought { get; set; } = 70;
    public int Mom_RsiMomentumMin { get; set; } = 40;
    public decimal Mom_AtrStopMultiplier { get; set; } = 2.0m;
    public decimal Mom_AtrTargetMultiplier { get; set; } = 3.0m;

    // MultiTimeframeTrend
    public int Mtf_LongTrendMaPeriod { get; set; } = 50;
    public int Mtf_ShortEntryMaPeriod { get; set; } = 20;
    public decimal Mtf_MaxPullbackPercent { get; set; } = 0.05m;
    public int Mtf_TrendConfirmationBars { get; set; } = 3;
    public decimal Mtf_MaxDistanceAboveShortMa { get; set; } = 0.02m;
    public decimal Mtf_AtrStopMultiplier { get; set; } = 1.5m;
    public decimal Mtf_AtrTargetMultiplier { get; set; } = 3.0m;

    // MeanReversionChannel
    public int Chan_EmaPeriod { get; set; } = 25;
    public int Chan_AtrPeriod { get; set; } = 10;
    public decimal Chan_AtrMultiplier { get; set; } = 1.5m;
    public int Chan_RsiPeriod { get; set; } = 14;
    public int Chan_RsiOversold { get; set; } = 40;
    public int Chan_RecentLowLookbackBars { get; set; } = 5;

    // Rsi2Bollinger
    public int Rsi2Bb_RsiPeriod { get; set; } = 2;
    public decimal Rsi2Bb_RsiThreshold { get; set; } = 20m;
    public int Rsi2Bb_BollingerPeriod { get; set; } = 20;
    public decimal Rsi2Bb_BollingerStdDev { get; set; } = 2.0m;
    public int Rsi2Bb_LongTrendMaPeriod { get; set; } = 200;
    public decimal Rsi2Bb_AtrStopMultiplier { get; set; } = 1.5m;

    // VolatilityBreakout
    public decimal VolBrk_BreakoutFactor { get; set; } = 0.5m;
    public decimal VolBrk_MinVolumeMultiplier { get; set; } = 1.0m;
    public int VolBrk_VolumeAvgPeriod { get; set; } = 20;
    public decimal VolBrk_AtrStopMultiplier { get; set; } = 2.0m;
    public decimal VolBrk_AtrTargetMultiplier { get; set; } = 3.0m;

    // Tqqq200Sma
    public int Tqqq_SmaPeriod { get; set; } = 200;
    public decimal Tqqq_OverheatPercent { get; set; } = 0.05m;
    public int Tqqq_ConfirmationDays { get; set; } = 2;
    public int Tqqq_ShortTrendEmaPeriod { get; set; } = 50;
    public int Tqqq_VolumeAvgPeriod { get; set; } = 20;
    public decimal Tqqq_MinVolumeRatio { get; set; } = 1.0m;
    public decimal Tqqq_AtrStopMultiplier { get; set; } = 3.0m;
    public decimal Tqqq_AtrTargetMultiplier { get; set; } = 8.0m;

    // ── 청산 전략 오버라이드 ──

    public int GapUp_ExitMaxHoldingBars { get; set; } = 3;
    public decimal GapUp_ExitTrailingAtr { get; set; } = 0m;
    public decimal GapUp_ExitPartialR { get; set; } = 2.0m;

    public int Breakout_ExitMaxHoldingBars { get; set; } = 15;
    public decimal Breakout_ExitTrailingAtr { get; set; } = 2.5m;
    public decimal Breakout_ExitPartialR { get; set; } = 2.5m;

    public int Vwap_ExitMaxHoldingBars { get; set; } = 3;
    public decimal Vwap_ExitTrailingAtr { get; set; } = 0m;
    public decimal Vwap_ExitPartialR { get; set; } = 1.5m;

    public int Rsi_ExitMaxHoldingBars { get; set; } = 5;
    public decimal Rsi_ExitTrailingAtr { get; set; } = 0m;
    public decimal Rsi_ExitPartialR { get; set; } = 1.5m;

    public int Trend_ExitMaxHoldingBars { get; set; } = 20;
    public decimal Trend_ExitTrailingAtr { get; set; } = 3.0m;
    public decimal Trend_ExitPartialR { get; set; } = 3.0m;

    public int Orb_ExitMaxHoldingBars { get; set; } = 3;
    public decimal Orb_ExitTrailingAtr { get; set; } = 0m;
    public decimal Orb_ExitPartialR { get; set; } = 2.0m;

    public int VolSpike_ExitMaxHoldingBars { get; set; } = 5;
    public decimal VolSpike_ExitTrailingAtr { get; set; } = 1.5m;
    public decimal VolSpike_ExitPartialR { get; set; } = 0m;

    public int Earnings_ExitMaxHoldingBars { get; set; } = 20;
    public decimal Earnings_ExitTrailingAtr { get; set; } = 2.5m;
    public decimal Earnings_ExitPartialR { get; set; } = 2.0m;

    public int Regime_ExitMaxHoldingBars { get; set; } = 15;
    public decimal Regime_ExitTrailingAtr { get; set; } = 2.5m;
    public decimal Regime_ExitPartialR { get; set; } = 2.0m;

    public int Vola_ExitMaxHoldingBars { get; set; } = 7;
    public decimal Vola_ExitTrailingAtr { get; set; } = 2.0m;
    public decimal Vola_ExitPartialR { get; set; } = 2.0m;

    public int Mom_ExitMaxHoldingBars { get; set; } = 10;
    public decimal Mom_ExitTrailingAtr { get; set; } = 2.5m;
    public decimal Mom_ExitPartialR { get; set; } = 2.0m;

    public int Mtf_ExitMaxHoldingBars { get; set; } = 30;
    public decimal Mtf_ExitTrailingAtr { get; set; } = 3.0m;
    public decimal Mtf_ExitPartialR { get; set; } = 3.0m;

    public int Chan_ExitMaxHoldingBars { get; set; } = 5;
    public decimal Chan_ExitTrailingAtr { get; set; } = 0m;
    public decimal Chan_ExitPartialR { get; set; } = 1.5m;

    public int Rsi2Bb_ExitMaxHoldingBars { get; set; } = 5;
    public decimal Rsi2Bb_ExitTrailingAtr { get; set; } = 0m;
    public decimal Rsi2Bb_ExitPartialR { get; set; } = 1.5m;

    public int VolBrk_ExitMaxHoldingBars { get; set; } = 5;
    public decimal VolBrk_ExitTrailingAtr { get; set; } = 2.0m;
    public decimal VolBrk_ExitPartialR { get; set; } = 0m;

    public int Tqqq_ExitMaxHoldingBars { get; set; } = 999;

    /// <summary>
    /// 현재 실거래 설정(PatternSettings + DB 청산 오버라이드)에서 ViewModel을 생성합니다.
    /// 설정 페이지에서 현재 적용된 파라미터를 편집할 때 사용.
    /// </summary>
    public static PatternParamViewModel FromLiveSettings(
        PatternSettings ps, PatternParameterOverrides? exitOverrides = null)
    {
        var vm = new PatternParamViewModel
        {
            // GapUpPullback
            GapUp_MinGapPercent = ps.GapUpPullback.MinGapPercent,
            GapUp_MaxPullbackPercent = ps.GapUpPullback.MaxPullbackPercent,
            GapUp_MinVolumeDecimal = ps.GapUpPullback.MinVolume / 10_000m,
            // Breakout
            Breakout_LookbackDays = ps.Breakout.LookbackDays,
            Breakout_MinVolumeMultiplier = ps.Breakout.MinVolumeMultiplier,
            Breakout_BreakoutMarginPercent = ps.Breakout.BreakoutMarginPercent,
            Breakout_AtrStopMultiplier = ps.Breakout.AtrStopMultiplier,
            Breakout_AtrTargetMultiplier = ps.Breakout.AtrTargetMultiplier,
            // VwapReversion
            Vwap_MaxDeviationPercent = ps.VwapReversion.MaxDeviationPercent,
            Vwap_MinBouncePercent = ps.VwapReversion.MinBouncePercent,
            Vwap_MinBounceFromLowPercent = ps.VwapReversion.MinBounceFromLowPercent,
            // RsiMeanReversion
            Rsi_OversoldThreshold = ps.RsiMeanReversion.OversoldThreshold,
            Rsi_Period = ps.RsiMeanReversion.Period,
            Rsi_MinVolumeIncreaseMultiplier = ps.RsiMeanReversion.MinVolumeIncreaseMultiplier,
            Rsi_AtrStopMultiplier = ps.RsiMeanReversion.AtrStopMultiplier,
            Rsi_AtrTargetMultiplier = ps.RsiMeanReversion.AtrTargetMultiplier,
            // TrendPullback
            Trend_MaPeriod = ps.TrendPullback.MaPeriod,
            Trend_MaxPullbackFromMa = ps.TrendPullback.MaxPullbackFromMa,
            Trend_TrendConfirmationDays = ps.TrendPullback.TrendConfirmationDays,
            Trend_AtrStopMultiplier = ps.TrendPullback.AtrStopMultiplier,
            Trend_AtrTargetMultiplier = ps.TrendPullback.AtrTargetMultiplier,
            // OpeningRangeBreakout
            Orb_RangeMinutes = ps.OpeningRangeBreakout.RangeMinutes,
            Orb_MinRangePercent = ps.OpeningRangeBreakout.MinRangePercent,
            // VolumeSpikeContinuation
            VolSpike_VolumeMultiplier = ps.VolumeSpikeContinuation.VolumeMultiplier,
            VolSpike_ContinuationBars = ps.VolumeSpikeContinuation.ContinuationBars,
            VolSpike_VolumeAvgPeriod = ps.VolumeSpikeContinuation.VolumeAvgPeriod,
            VolSpike_AtrStopMultiplier = ps.VolumeSpikeContinuation.AtrStopMultiplier,
            VolSpike_AtrTargetMultiplier = ps.VolumeSpikeContinuation.AtrTargetMultiplier,
            // EarningsDrift
            Earnings_DriftDays = ps.EarningsDrift.DriftDays,
            Earnings_MinSurprisePercent = ps.EarningsDrift.MinSurprisePercent,
            // IndexRegimeFilter
            Regime_MaPeriod = ps.IndexRegimeFilter.MaPeriod,
            Regime_IndexSymbol = ps.IndexRegimeFilter.IndexSymbol,
            // VolatilityExpansion
            Vola_BollingerPeriod = ps.VolatilityExpansion.BollingerPeriod,
            Vola_StdDevMultiplier = ps.VolatilityExpansion.StdDevMultiplier,
            Vola_AtrStopMultiplier = ps.VolatilityExpansion.AtrStopMultiplier,
            Vola_AtrTargetMultiplier = ps.VolatilityExpansion.AtrTargetMultiplier,
            // MomentumReversal
            Mom_FastEmaPeriod = ps.MomentumReversal.FastEmaPeriod,
            Mom_SlowEmaPeriod = ps.MomentumReversal.SlowEmaPeriod,
            Mom_MacdSignalPeriod = ps.MomentumReversal.MacdSignalPeriod,
            Mom_RsiPeriod = ps.MomentumReversal.RsiPeriod,
            Mom_RsiOversold = ps.MomentumReversal.RsiOversold,
            Mom_RsiOverbought = ps.MomentumReversal.RsiOverbought,
            Mom_RsiMomentumMin = ps.MomentumReversal.RsiMomentumMin,
            Mom_AtrStopMultiplier = ps.MomentumReversal.AtrStopMultiplier,
            Mom_AtrTargetMultiplier = ps.MomentumReversal.AtrTargetMultiplier,
            // MultiTimeframeTrend
            Mtf_LongTrendMaPeriod = ps.MultiTimeframeTrend.LongTrendMaPeriod,
            Mtf_ShortEntryMaPeriod = ps.MultiTimeframeTrend.ShortEntryMaPeriod,
            Mtf_MaxPullbackPercent = ps.MultiTimeframeTrend.MaxPullbackPercent,
            Mtf_TrendConfirmationBars = ps.MultiTimeframeTrend.TrendConfirmationBars,
            Mtf_MaxDistanceAboveShortMa = ps.MultiTimeframeTrend.MaxDistanceAboveShortMa,
            Mtf_AtrStopMultiplier = ps.MultiTimeframeTrend.AtrStopMultiplier,
            Mtf_AtrTargetMultiplier = ps.MultiTimeframeTrend.AtrTargetMultiplier,
            // MeanReversionChannel
            Chan_EmaPeriod = ps.MeanReversionChannel.EmaPeriod,
            Chan_AtrPeriod = ps.MeanReversionChannel.AtrPeriod,
            Chan_AtrMultiplier = ps.MeanReversionChannel.AtrMultiplier,
            Chan_RsiPeriod = ps.MeanReversionChannel.RsiPeriod,
            Chan_RsiOversold = ps.MeanReversionChannel.RsiOversold,
            Chan_RecentLowLookbackBars = ps.MeanReversionChannel.RecentLowLookbackBars,
            // Rsi2Bollinger
            Rsi2Bb_RsiPeriod = ps.Rsi2Bollinger.RsiPeriod,
            Rsi2Bb_RsiThreshold = ps.Rsi2Bollinger.RsiThreshold,
            Rsi2Bb_BollingerPeriod = ps.Rsi2Bollinger.BollingerPeriod,
            Rsi2Bb_BollingerStdDev = ps.Rsi2Bollinger.BollingerStdDev,
            Rsi2Bb_LongTrendMaPeriod = ps.Rsi2Bollinger.LongTrendMaPeriod,
            Rsi2Bb_AtrStopMultiplier = ps.Rsi2Bollinger.AtrStopMultiplier,
            // VolatilityBreakout
            VolBrk_BreakoutFactor = ps.VolatilityBreakout.BreakoutFactor,
            VolBrk_MinVolumeMultiplier = ps.VolatilityBreakout.MinVolumeMultiplier,
            VolBrk_VolumeAvgPeriod = ps.VolatilityBreakout.VolumeAvgPeriod,
            VolBrk_AtrStopMultiplier = ps.VolatilityBreakout.AtrStopMultiplier,
            VolBrk_AtrTargetMultiplier = ps.VolatilityBreakout.AtrTargetMultiplier,
            // Tqqq200Sma
            Tqqq_SmaPeriod = ps.Tqqq200Sma.SmaPeriod,
            Tqqq_OverheatPercent = ps.Tqqq200Sma.OverheatPercent,
            Tqqq_ConfirmationDays = ps.Tqqq200Sma.ConfirmationDays,
            Tqqq_ShortTrendEmaPeriod = ps.Tqqq200Sma.ShortTrendEmaPeriod,
            Tqqq_VolumeAvgPeriod = ps.Tqqq200Sma.VolumeAvgPeriod,
            Tqqq_MinVolumeRatio = ps.Tqqq200Sma.MinVolumeRatio,
            Tqqq_AtrStopMultiplier = ps.Tqqq200Sma.AtrStopMultiplier,
            Tqqq_AtrTargetMultiplier = ps.Tqqq200Sma.AtrTargetMultiplier,
        };

        // DB에 저장된 청산 오버라이드가 있으면 적용
        if (exitOverrides != null)
        {
            foreach (var ovProp in OverrideProps)
            {
                if (!ovProp.Name.Contains("Exit")) continue;
                var value = ovProp.GetValue(exitOverrides);
                if (value == null) continue;
                if (ViewModelProps.TryGetValue(ovProp.Name, out var vmProp))
                {
                    vmProp.SetValue(vm, Convert.ChangeType(value,
                        Nullable.GetUnderlyingType(vmProp.PropertyType) ?? vmProp.PropertyType));
                }
            }
        }

        return vm;
    }

    // ── UI 필드 정의 (Settings + BacktestResults 공유) ──

    /// <summary>패턴 파라미터 필드 메타데이터. 양쪽 페이지에서 동일한 라벨/포맷 사용.</summary>
    public sealed record FieldDef(string Label, string VmPropName, string Category = "detect")
    {
        public string Format { get; init; } = "F2";
        public decimal DecStep { get; init; } = 0.01m;
        public string? HelperText { get; init; }
    }

    /// <summary>ViewModel 프로퍼티 리플렉션 캐시 (UI 렌더링용)</summary>
    public static readonly Dictionary<string, PropertyInfo> FieldPropCache =
        typeof(PatternParamViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name);

    /// <summary>패턴별 편집 가능 필드 정의. 라벨/순서/포맷이 Settings와 BacktestResults에서 동일하게 사용됩니다.</summary>
    public static readonly Dictionary<PatternType, List<FieldDef>> PatternFieldDefs = new()
    {
        [PatternType.GapUpPullback] =
        [
            new("최소 갭 비율", "GapUp_MinGapPercent") { Format = "P2", DecStep = 0.005m, HelperText = "전일 종가 대비 갭업 최소 비율" },
            new("최대 풀백 비율", "GapUp_MaxPullbackPercent") { Format = "P2", DecStep = 0.05m, HelperText = "고가 대비 현재가 풀백 최소 비율" },
            new("최소 거래량 (만주)", "GapUp_MinVolumeDecimal") { Format = "N0", DecStep = 10m, HelperText = "최소 거래량 (만주 단위)" },
            new("최대 보유 봉", "GapUp_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "GapUp_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "GapUp_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.Breakout] =
        [
            new("룩백 기간 (일)", "Breakout_LookbackDays") { HelperText = "52주 신고가 계산 기간" },
            new("최소 거래량 배율", "Breakout_MinVolumeMultiplier") { Format = "F1", DecStep = 0.1m, HelperText = "평균 거래량 대비 배율" },
            new("돌파 마진 비율", "Breakout_BreakoutMarginPercent") { Format = "P3", DecStep = 0.0005m, HelperText = "52주 고가 초과 최소 마진" },
            new("ATR 손절 배수", "Breakout_AtrStopMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 - ATR x N" },
            new("ATR 목표 배수", "Breakout_AtrTargetMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 + ATR x N" },
            new("최대 보유 봉", "Breakout_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "Breakout_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "Breakout_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.VwapReversion] =
        [
            new("최대 이탈 비율", "Vwap_MaxDeviationPercent") { Format = "P2", DecStep = 0.005m, HelperText = "VWAP 대비 하방 이탈" },
            new("최소 반등 비율", "Vwap_MinBouncePercent") { Format = "P2", DecStep = 0.001m, HelperText = "저점 대비 반등" },
            new("저가 대비 반등 비율", "Vwap_MinBounceFromLowPercent") { Format = "P2", DecStep = 0.05m, HelperText = "일중 저가 대비 반등" },
            new("최대 보유 봉", "Vwap_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "Vwap_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "Vwap_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.RsiMeanReversion] =
        [
            new("RSI 기간", "Rsi_Period") { HelperText = "RSI 계산 기간" },
            new("과매도 임계값", "Rsi_OversoldThreshold") { HelperText = "이 값 이하에서 매수 신호" },
            new("최소 거래량 증가 배율", "Rsi_MinVolumeIncreaseMultiplier") { Format = "F1", DecStep = 0.1m, HelperText = "평균 거래량 대비" },
            new("ATR 손절 배수", "Rsi_AtrStopMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 - ATR x N" },
            new("ATR 목표 배수", "Rsi_AtrTargetMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 + ATR x N" },
            new("최대 보유 봉", "Rsi_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "Rsi_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "Rsi_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.TrendPullback] =
        [
            new("이동평균 기간", "Trend_MaPeriod") { HelperText = "추세 판단 MA 기간" },
            new("MA 대비 최대 풀백", "Trend_MaxPullbackFromMa") { Format = "P2", DecStep = 0.005m, HelperText = "MA 근접 매수 조건" },
            new("추세 확인 기간", "Trend_TrendConfirmationDays") { HelperText = "상승추세 확인 일수" },
            new("ATR 손절 배수", "Trend_AtrStopMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 - ATR x N" },
            new("ATR 목표 배수", "Trend_AtrTargetMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 + ATR x N" },
            new("최대 보유 봉", "Trend_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "Trend_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "Trend_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.OpeningRangeBreakout] =
        [
            new("범위 측정 기간 (분)", "Orb_RangeMinutes") { HelperText = "개장 후 범위 측정 시간" },
            new("최소 범위 비율", "Orb_MinRangePercent") { Format = "P2", DecStep = 0.001m, HelperText = "최소 범위 폭" },
            new("최대 보유 봉", "Orb_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "Orb_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "Orb_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.VolumeSpikeContinuation] =
        [
            new("거래량 배율 임계값", "VolSpike_VolumeMultiplier") { Format = "F1", DecStep = 0.1m, HelperText = "평균 거래량 대비 스파이크 배율" },
            new("지속 봉 수", "VolSpike_ContinuationBars") { HelperText = "상승 지속 확인 봉 수" },
            new("거래량 평균 기간", "VolSpike_VolumeAvgPeriod") { HelperText = "거래량 평균 계산 기간" },
            new("ATR 손절 배수", "VolSpike_AtrStopMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 - ATR x N" },
            new("ATR 목표 배수", "VolSpike_AtrTargetMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 + ATR x N" },
            new("최대 보유 봉", "VolSpike_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "VolSpike_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "VolSpike_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.EarningsDrift] =
        [
            new("드리프트 기간 (일)", "Earnings_DriftDays") { HelperText = "실적 발표 후 추적 기간" },
            new("최소 서프라이즈 비율", "Earnings_MinSurprisePercent") { Format = "P2", DecStep = 0.01m, HelperText = "어닝 서프라이즈 최소 비율" },
            new("최대 보유 봉", "Earnings_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "Earnings_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "Earnings_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.IndexRegimeFilter] =
        [
            new("이동평균 기간", "Regime_MaPeriod") { HelperText = "레짐 판단 MA 기간" },
            new("지수 심볼", "Regime_IndexSymbol") { HelperText = "기준 지수 (예: SPY)" },
            new("최대 보유 봉", "Regime_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "Regime_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "Regime_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.VolatilityExpansion] =
        [
            new("볼린저 밴드 기간", "Vola_BollingerPeriod") { HelperText = "볼린저 밴드 계산 기간" },
            new("표준편차 배수", "Vola_StdDevMultiplier") { Format = "F1", DecStep = 0.1m, HelperText = "밴드 폭 (기본 2.0)" },
            new("ATR 손절 배수", "Vola_AtrStopMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 - ATR x N" },
            new("ATR 목표 배수", "Vola_AtrTargetMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 + ATR x N" },
            new("최대 보유 봉", "Vola_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "Vola_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "Vola_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.MomentumReversal] =
        [
            new("빠른 EMA 기간", "Mom_FastEmaPeriod") { HelperText = "MACD 빠른선" },
            new("느린 EMA 기간", "Mom_SlowEmaPeriod") { HelperText = "MACD 느린선" },
            new("MACD 시그널 기간", "Mom_MacdSignalPeriod") { HelperText = "시그널선 기간" },
            new("RSI 기간", "Mom_RsiPeriod") { HelperText = "RSI 계산 기간" },
            new("RSI 과매도", "Mom_RsiOversold") { HelperText = "과매도 임계값" },
            new("RSI 과매수", "Mom_RsiOverbought") { HelperText = "과매수 임계값" },
            new("RSI 모멘텀 하한", "Mom_RsiMomentumMin") { HelperText = "모멘텀 확인 하한" },
            new("ATR 손절 배수", "Mom_AtrStopMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 - ATR x N" },
            new("ATR 목표 배수", "Mom_AtrTargetMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 + ATR x N" },
            new("최대 보유 봉", "Mom_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "Mom_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "Mom_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.MultiTimeframeTrend] =
        [
            new("장기 추세 MA 기간", "Mtf_LongTrendMaPeriod") { HelperText = "장기 추세 확인용" },
            new("단기 진입 MA 기간", "Mtf_ShortEntryMaPeriod") { HelperText = "단기 진입 타이밍용" },
            new("최대 풀백 비율", "Mtf_MaxPullbackPercent") { Format = "P2", DecStep = 0.005m, HelperText = "단기MA 근접 매수 조건" },
            new("추세 확인 봉 수", "Mtf_TrendConfirmationBars") { HelperText = "상승추세 확인 봉" },
            new("단기MA 위 최대거리", "Mtf_MaxDistanceAboveShortMa") { Format = "P2", DecStep = 0.005m, HelperText = "과열 방지" },
            new("ATR 손절 배수", "Mtf_AtrStopMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 - ATR x N" },
            new("ATR 목표 배수", "Mtf_AtrTargetMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 + ATR x N" },
            new("최대 보유 봉", "Mtf_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "Mtf_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "Mtf_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.MeanReversionChannel] =
        [
            new("EMA 기간", "Chan_EmaPeriod") { HelperText = "채널 중심선 기간" },
            new("ATR 기간", "Chan_AtrPeriod") { HelperText = "ATR 계산 기간" },
            new("ATR 배수", "Chan_AtrMultiplier") { Format = "F1", DecStep = 0.1m, HelperText = "채널 폭 배수" },
            new("RSI 기간", "Chan_RsiPeriod") { HelperText = "RSI 계산 기간" },
            new("RSI 과매도 임계값", "Chan_RsiOversold") { HelperText = "과매도 확인" },
            new("최근 저가 룩백", "Chan_RecentLowLookbackBars") { HelperText = "최근 저가 조회 기간" },
            new("최대 보유 봉", "Chan_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "Chan_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "Chan_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.Rsi2Bollinger] =
        [
            new("RSI 기간", "Rsi2Bb_RsiPeriod") { HelperText = "RSI 계산 기간" },
            new("RSI 임계값", "Rsi2Bb_RsiThreshold") { Format = "F0", DecStep = 5m, HelperText = "매수 진입 임계값" },
            new("볼린저 기간", "Rsi2Bb_BollingerPeriod") { HelperText = "볼린저 밴드 기간" },
            new("볼린저 표준편차", "Rsi2Bb_BollingerStdDev") { Format = "F1", DecStep = 0.1m, HelperText = "밴드 폭" },
            new("장기 추세 SMA", "Rsi2Bb_LongTrendMaPeriod") { HelperText = "추세 필터 기간" },
            new("ATR 손절 배수", "Rsi2Bb_AtrStopMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 - ATR x N" },
            new("최대 보유 봉", "Rsi2Bb_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "Rsi2Bb_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "Rsi2Bb_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.VolatilityBreakout] =
        [
            new("돌파 계수 (K)", "VolBrk_BreakoutFactor") { Format = "F2", DecStep = 0.05m, HelperText = "전일 변동성 x K 돌파" },
            new("최소 거래량 배수", "VolBrk_MinVolumeMultiplier") { Format = "F1", DecStep = 0.1m, HelperText = "평균 거래량 대비" },
            new("거래량 평균 기간", "VolBrk_VolumeAvgPeriod") { HelperText = "거래량 평균 계산 기간" },
            new("ATR 손절 배수", "VolBrk_AtrStopMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 - ATR x N" },
            new("ATR 목표 배수", "VolBrk_AtrTargetMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "진입가 + ATR x N" },
            new("최대 보유 봉", "VolBrk_ExitMaxHoldingBars", "exit") { HelperText = "시간 청산 기한" },
            new("트레일링 ATR", "VolBrk_ExitTrailingAtr", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
            new("부분익절 R배수", "VolBrk_ExitPartialR", "exit") { Format = "F1", DecStep = 0.5m, HelperText = "0 = 비활성" },
        ],
        [PatternType.Tqqq200Sma] =
        [
            new("SMA 기간", "Tqqq_SmaPeriod") { HelperText = "장기 추세 SMA 기간" },
            new("과열 임계값", "Tqqq_OverheatPercent") { Format = "P2", DecStep = 0.005m, HelperText = "SMA 대비 과열 비율" },
            new("확인 일수", "Tqqq_ConfirmationDays") { HelperText = "SMA 돌파 확인 기간" },
            new("단기 EMA", "Tqqq_ShortTrendEmaPeriod") { HelperText = "단기 추세 EMA 기간" },
            new("거래량 평균 기간", "Tqqq_VolumeAvgPeriod") { HelperText = "거래량 평균 계산 기간" },
            new("최소 거래량 배수", "Tqqq_MinVolumeRatio") { Format = "F1", DecStep = 0.1m, HelperText = "평균 거래량 대비" },
            new("ATR 손절 배수", "Tqqq_AtrStopMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "넓은 손절 → 트레일링 위임" },
            new("ATR 목표 배수", "Tqqq_AtrTargetMultiplier") { Format = "F1", DecStep = 0.5m, HelperText = "넓은 목표 → 트레일링 위임" },
            new("최대 보유 봉", "Tqqq_ExitMaxHoldingBars", "exit") { HelperText = "999 = 레짐 기반 무제한 보유" },
        ],
    };

    // ── 리플렉션 기반 ToOverrides() ──

    private static readonly PropertyInfo[] OverrideProps =
        typeof(PatternParameterOverrides).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private static readonly Dictionary<string, PropertyInfo> ViewModelProps;

    static PatternParamViewModel()
    {
        var props = typeof(PatternParamViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        ViewModelProps = new Dictionary<string, PropertyInfo>(props.Length);
        foreach (var p in props)
            ViewModelProps[p.Name] = p;
    }

    /// <summary>
    /// 리플렉션으로 동일한 이름의 프로퍼티를 자동 매핑하여 PatternParameterOverrides를 생성합니다.
    /// GapUp_MinVolume은 계산 프로퍼티로 특수 처리됩니다.
    /// </summary>
    public PatternParameterOverrides ToOverrides()
    {
        var result = new PatternParameterOverrides();

        foreach (var ovProp in OverrideProps)
        {
            if (ViewModelProps.TryGetValue(ovProp.Name, out var vmProp))
            {
                var value = vmProp.GetValue(this);
                if (value != null)
                {
                    // ViewModel은 non-nullable, Override는 nullable → Convert 처리
                    ovProp.SetValue(result, Convert.ChangeType(value, Nullable.GetUnderlyingType(ovProp.PropertyType) ?? ovProp.PropertyType));
                }
            }
        }

        // GapUp_MinVolume은 계산 프로퍼티 (GapUp_MinVolumeDecimal × 10000)
        result.GapUp_MinVolume = GapUp_MinVolume;

        return result;
    }
}

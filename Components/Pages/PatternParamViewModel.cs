using System.Reflection;
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

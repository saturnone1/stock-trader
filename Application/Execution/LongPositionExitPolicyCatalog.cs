using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.Execution;

/// <summary>
/// 기본 패턴과 사용자 정의 전략을 실행 가능한 롱 포지션 청산 정책으로 변환하는 단일 카탈로그입니다.
/// 미리보기, 백테스트, 실시간 실행이 동일한 기본값과 활성화 규칙을 사용하게 합니다.
/// </summary>
public static class LongPositionExitPolicyCatalog
{
    public static LongPositionExitPolicy ForCustom(CustomPatternDefinition definition) => new(
        definition.MaxHoldingBars,
        EnableTrailingStop: definition.TrailingAtr > 0,
        TrailingStopAtrMultiplier: definition.TrailingAtr,
        TrailingActivationR: 1m,
        EnablePartialProfit: definition.PartialProfitR > 0,
        PartialProfitRMultiple: definition.PartialProfitR,
        EnableTargetExit: true,
        EnableTimeExit: true);

    public static LongPositionExitPolicy ForPattern(
        PatternType patternType,
        PatternParameterOverrides? overrides = null)
    {
        var baseline = GetBaseline(patternType);
        if (overrides == null) return baseline;

        var (maxBars, trailingAtr, partialProfitR) = patternType switch
        {
            PatternType.GapUpPullback => (overrides.GapUp_ExitMaxHoldingBars, overrides.GapUp_ExitTrailingAtr, overrides.GapUp_ExitPartialR),
            PatternType.Breakout => (overrides.Breakout_ExitMaxHoldingBars, overrides.Breakout_ExitTrailingAtr, overrides.Breakout_ExitPartialR),
            PatternType.VwapReversion => (overrides.Vwap_ExitMaxHoldingBars, overrides.Vwap_ExitTrailingAtr, overrides.Vwap_ExitPartialR),
            PatternType.RsiMeanReversion => (overrides.Rsi_ExitMaxHoldingBars, overrides.Rsi_ExitTrailingAtr, overrides.Rsi_ExitPartialR),
            PatternType.TrendPullback => (overrides.Trend_ExitMaxHoldingBars, overrides.Trend_ExitTrailingAtr, overrides.Trend_ExitPartialR),
            PatternType.OpeningRangeBreakout => (overrides.Orb_ExitMaxHoldingBars, overrides.Orb_ExitTrailingAtr, overrides.Orb_ExitPartialR),
            PatternType.VolumeSpikeContinuation => (overrides.VolSpike_ExitMaxHoldingBars, overrides.VolSpike_ExitTrailingAtr, overrides.VolSpike_ExitPartialR),
            PatternType.EarningsDrift => (overrides.Earnings_ExitMaxHoldingBars, overrides.Earnings_ExitTrailingAtr, overrides.Earnings_ExitPartialR),
            PatternType.IndexRegimeFilter => (overrides.Regime_ExitMaxHoldingBars, overrides.Regime_ExitTrailingAtr, overrides.Regime_ExitPartialR),
            PatternType.VolatilityExpansion => (overrides.Vola_ExitMaxHoldingBars, overrides.Vola_ExitTrailingAtr, overrides.Vola_ExitPartialR),
            PatternType.MomentumReversal => (overrides.Mom_ExitMaxHoldingBars, overrides.Mom_ExitTrailingAtr, overrides.Mom_ExitPartialR),
            PatternType.MultiTimeframeTrend => (overrides.Mtf_ExitMaxHoldingBars, overrides.Mtf_ExitTrailingAtr, overrides.Mtf_ExitPartialR),
            PatternType.MeanReversionChannel => (overrides.Chan_ExitMaxHoldingBars, overrides.Chan_ExitTrailingAtr, overrides.Chan_ExitPartialR),
            PatternType.Rsi2Bollinger => (overrides.Rsi2Bb_ExitMaxHoldingBars, overrides.Rsi2Bb_ExitTrailingAtr, overrides.Rsi2Bb_ExitPartialR),
            PatternType.CumulativeRsi2 => (overrides.CumRsi2_ExitMaxHoldingBars, overrides.CumRsi2_ExitTrailingAtr, overrides.CumRsi2_ExitPartialR),
            PatternType.VolatilityBreakout => (overrides.VolBrk_ExitMaxHoldingBars, overrides.VolBrk_ExitTrailingAtr, overrides.VolBrk_ExitPartialR),
            PatternType.Tqqq200Sma => (overrides.Tqqq_ExitMaxHoldingBars, (decimal?)null, (decimal?)null),
            _ => ((int?)null, (decimal?)null, (decimal?)null)
        };

        if (maxBars == null && trailingAtr == null && partialProfitR == null)
            return baseline;

        return baseline with
        {
            MaxHoldingBars = maxBars ?? baseline.MaxHoldingBars,
            EnableTrailingStop = trailingAtr.HasValue
                ? trailingAtr.Value > 0
                : baseline.EnableTrailingStop,
            TrailingStopAtrMultiplier = trailingAtr ?? baseline.TrailingStopAtrMultiplier,
            EnablePartialProfit = partialProfitR.HasValue
                ? partialProfitR.Value > 0
                : baseline.EnablePartialProfit,
            PartialProfitRMultiple = partialProfitR ?? baseline.PartialProfitRMultiple
        };
    }

    private static LongPositionExitPolicy GetBaseline(PatternType patternType) => patternType switch
    {
        PatternType.GapUpPullback => new(3, false, 0m, 0m, true, 2.0m, true, true),
        PatternType.VwapReversion => new(3, false, 0m, 0m, true, 1.5m, true, true),
        PatternType.OpeningRangeBreakout => new(3, false, 0m, 0m, true, 2.0m, true, true),
        PatternType.VolumeSpikeContinuation => new(5, true, 1.5m, 1.0m, false, 0m, true, true),
        PatternType.VolatilityBreakout => new(5, true, 2.0m, 1.0m, false, 0m, true, true),
        PatternType.RsiMeanReversion => new(5, false, 0m, 0m, true, 1.5m, true, true),
        PatternType.VolatilityExpansion => new(7, true, 2.0m, 1.5m, true, 2.0m, true, true),
        PatternType.MeanReversionChannel => new(5, false, 0m, 0m, true, 1.5m, true, true),
        PatternType.Rsi2Bollinger => new(5, false, 0m, 0m, true, 1.5m, true, true),
        PatternType.CumulativeRsi2 => new(20, false, 0m, 0m, false, 0m, false, false, 0m),
        PatternType.Breakout => new(15, true, 2.5m, 1.5m, true, 2.5m, true, true),
        PatternType.MomentumReversal => new(10, true, 2.5m, 1.5m, true, 2.0m, true, true),
        PatternType.IndexRegimeFilter => new(15, true, 2.5m, 1.5m, true, 2.0m, true, true),
        PatternType.TrendPullback => new(20, true, 3.0m, 2.0m, true, 3.0m, true, true),
        PatternType.EarningsDrift => new(20, true, 2.5m, 1.5m, true, 2.0m, true, true),
        PatternType.MultiTimeframeTrend => new(30, true, 3.0m, 2.0m, true, 3.0m, true, true),
        PatternType.Tqqq200Sma => new(999, false, 0m, 0m, false, 0m, false, false),
        _ => new(20, true, 2.5m, 1.0m, true, 2.0m, true, true)
    };
}

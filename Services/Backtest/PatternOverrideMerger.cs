using System.Reflection;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.Backtest;

/// <summary>
/// PatternParameterOverrides의 flat 프로퍼티를 PatternSettings의 중첩 Config 객체에
/// 리플렉션으로 자동 매핑합니다. "{Prefix}_{ConfigPropertyName}" 규칙을 따릅니다.
/// Exit 관련 오버라이드(_Exit*)는 PatternExitProfile에서 별도 처리하므로 제외됩니다.
/// </summary>
internal static class PatternOverrideMerger
{
    /// <summary>Override 프로퍼티 접두사 → PatternSettings 프로퍼티 이름 매핑</summary>
    private static readonly (string Prefix, string ConfigProperty)[] Mappings =
    [
        ("GapUp",    nameof(PatternSettings.GapUpPullback)),
        ("Breakout", nameof(PatternSettings.Breakout)),
        ("Vwap",     nameof(PatternSettings.VwapReversion)),
        ("Rsi",      nameof(PatternSettings.RsiMeanReversion)),
        ("Trend",    nameof(PatternSettings.TrendPullback)),
        ("Orb",      nameof(PatternSettings.OpeningRangeBreakout)),
        ("VolSpike", nameof(PatternSettings.VolumeSpikeContinuation)),
        ("Earnings", nameof(PatternSettings.EarningsDrift)),
        ("Regime",   nameof(PatternSettings.IndexRegimeFilter)),
        ("Vola",     nameof(PatternSettings.VolatilityExpansion)),
        ("Mom",      nameof(PatternSettings.MomentumReversal)),
        ("Mtf",      nameof(PatternSettings.MultiTimeframeTrend)),
        ("Chan",     nameof(PatternSettings.MeanReversionChannel)),
        ("Rsi2Bb",   nameof(PatternSettings.Rsi2Bollinger)),
        ("CumRsi2",  nameof(PatternSettings.CumulativeRsi2)),
        ("VolBrk",   nameof(PatternSettings.VolatilityBreakout)),
        ("Tqqq",     nameof(PatternSettings.Tqqq200Sma)),
    ];

    private static readonly PropertyInfo[] OverrideProperties =
        typeof(PatternParameterOverrides).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    public static PatternSettings Merge(PatternSettings baseSettings, PatternParameterOverrides overrides)
    {
        var result = new PatternSettings { EnabledPatterns = baseSettings.EnabledPatterns };

        foreach (var (prefix, configName) in Mappings)
        {
            var settingsProp = typeof(PatternSettings).GetProperty(configName)!;
            var baseConfig = settingsProp.GetValue(baseSettings)!;
            var configType = settingsProp.PropertyType;

            // Deep-clone: 새 인스턴스 생성 후 base 값 복사
            var newConfig = Activator.CreateInstance(configType)!;
            foreach (var prop in configType.GetProperties())
            {
                prop.SetValue(newConfig, prop.GetValue(baseConfig));
            }

            // Override 프로퍼티 중 "{prefix}_{name}" 패턴 매칭 (Exit 제외)
            var prefixUnderscore = prefix + "_";
            foreach (var ovProp in OverrideProperties)
            {
                if (!ovProp.Name.StartsWith(prefixUnderscore)) continue;
                var suffix = ovProp.Name[prefixUnderscore.Length..];
                if (suffix.StartsWith("Exit")) continue;

                var targetProp = configType.GetProperty(suffix);
                if (targetProp == null) continue;

                var value = ovProp.GetValue(overrides);
                if (value == null) continue;

                targetProp.SetValue(newConfig, value);
            }

            settingsProp.SetValue(result, newConfig);
        }

        return result;
    }
}

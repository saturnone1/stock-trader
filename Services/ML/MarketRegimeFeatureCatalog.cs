using StockTrader.Application.MachineLearning;

namespace StockTrader.Services.ML;

/// <summary>시장 레짐 모델 피처 순서와 ML.NET 입력 변환의 단일 소유자입니다.</summary>
internal static class MarketRegimeFeatureCatalog
{
    public static readonly string[] ColumnNames =
    [
        nameof(RegimeFeatureInput.Return5Day),
        nameof(RegimeFeatureInput.Return10Day),
        nameof(RegimeFeatureInput.Return20Day),
        nameof(RegimeFeatureInput.VolatilityLevel),
        nameof(RegimeFeatureInput.VolumeChangeRate),
        nameof(RegimeFeatureInput.MaSlopePercent),
        nameof(RegimeFeatureInput.Rsi),
    ];

    static MarketRegimeFeatureCatalog()
    {
        if (ColumnNames.Length != MarketRegimeFeatureSchema.FeatureCount)
        {
            throw new InvalidOperationException(
                "Market regime feature catalog count does not match its persisted schema.");
        }
    }

    public static RegimeFeatureInput ToModelInput(MarketRegimeFeatures features) => new()
    {
        Return5Day = features.Return5Day,
        Return10Day = features.Return10Day,
        Return20Day = features.Return20Day,
        VolatilityLevel = features.VolatilityLevel,
        VolumeChangeRate = features.VolumeChangeRate,
        MaSlopePercent = features.MaSlopePercent,
        Rsi = features.Rsi,
    };
}

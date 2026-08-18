namespace StockTrader.Application.MachineLearning;

/// <summary>시장 레짐 모델 입력 벡터의 영속 스키마입니다.</summary>
public static class MarketRegimeFeatureSchema
{
    public const int CurrentVersion = 1;
    public const int FeatureCount = 7;
}

/// <summary>K-Means가 구분하는 투자자 관점의 시장 국면과 개수를 소유합니다.</summary>
public static class MarketRegimeClusterCatalog
{
    public const int RequiredClusterCount = 4;
    public const string Bullish = "강세장";
    public const string Bearish = "약세장";
    public const string Sideways = "횡보장";
    public const string HighVolatility = "고변동장";

    public static readonly IReadOnlySet<string> Labels = new HashSet<string>(
        [Bullish, Bearish, Sideways, HighVolatility],
        StringComparer.Ordinal);
}

/// <summary>한 완료 일봉 시점에서 계산한 인과적 시장 레짐 피처입니다.</summary>
public sealed record MarketRegimeFeatures(
    float Return5Day,
    float Return10Day,
    float Return20Day,
    float VolatilityLevel,
    float VolumeChangeRate,
    float MaSlopePercent,
    float Rsi);

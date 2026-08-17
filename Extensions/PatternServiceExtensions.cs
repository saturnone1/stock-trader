using StockTrader.Services.Patterns;

namespace StockTrader.Extensions;

public static class PatternServiceExtensions
{
    public static IServiceCollection AddPatternServices(this IServiceCollection services)
    {
        services.AddSingleton<ICustomStrategyDetectorFactory, CustomStrategyDetectorFactory>();
        services.AddScoped<IPatternDetector, GapUpPullbackDetector>();
        services.AddScoped<IPatternDetector, BreakoutDetector>();
        services.AddScoped<IPatternDetector, VwapReversionDetector>();
        services.AddScoped<IPatternDetector, RsiMeanReversionDetector>();
        services.AddScoped<IPatternDetector, TrendPullbackDetector>();
        services.AddScoped<IPatternDetector, OrbDetector>();
        services.AddScoped<IPatternDetector, VolumeSpikeContinuationDetector>();
        services.AddScoped<IPatternDetector, EarningsDriftDetector>();
        services.AddScoped<IPatternDetector, IndexRegimeFilterDetector>();
        services.AddScoped<IPatternDetector, VolatilityExpansionDetector>();
        services.AddScoped<IPatternDetector, MomentumReversalDetector>();
        services.AddScoped<IPatternDetector, MultiTimeframeTrendDetector>();
        services.AddScoped<IPatternDetector, MeanReversionChannelDetector>();
        services.AddScoped<IPatternDetector, Rsi2BollingerDetector>();
        services.AddScoped<IPatternDetector, CumulativeRsi2Detector>();
        services.AddScoped<IPatternDetector, VolatilityBreakoutDetector>();
        services.AddScoped<PatternDetectionService>();

        return services;
    }
}

using StockTrader.Services.Patterns;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Application.Trading;

namespace StockTrader.Extensions;

public static class PatternServiceExtensions
{
    public static IServiceCollection AddPatternServices(this IServiceCollection services)
    {
        services.AddSingleton<ICustomStrategyDetectorFactory, CustomStrategyDetectorFactory>();
        services.AddScoped<IBuiltInPatternDetectorFactory, BuiltInPatternDetectorFactory>();
        foreach (var descriptor in BuiltInPatternDetectorCatalog.All)
        {
            services.AddScoped(typeof(IPatternDetector), provider =>
                provider.GetRequiredService<IBuiltInPatternDetectorFactory>().Create(
                    descriptor.PatternType,
                    provider.GetRequiredService<IOptionsSnapshot<PatternSettings>>().Value));
        }
        services.AddScoped<PatternDetectionService>();
        services.AddScoped<ILivePatternDetection>(provider =>
            provider.GetRequiredService<PatternDetectionService>());
        services.AddScoped<ILiveMarketRegimeEvaluator, LiveMarketRegimeEvaluator>();
        services.AddSingleton<LivePatternScanState>();
        services.AddScoped<ILivePatternScanCycle, LivePatternScanCycle>();

        return services;
    }
}

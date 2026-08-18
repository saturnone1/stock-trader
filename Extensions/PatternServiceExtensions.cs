using StockTrader.Services.Patterns;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;

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

        return services;
    }
}

using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models.Enums;

namespace StockTrader.Services.DataFeed;

public interface IDataFeedServiceFactory
{
    Task<IDataFeedService> GetServiceAsync(CancellationToken ct = default);
    IDataFeedService GetService(DataSource dataSource);
}

public class DataFeedServiceFactory : IDataFeedServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISettingsRepository _settingsRepo;
    private readonly AlpacaSettings _alpacaSettings;
    private readonly ILogger<DataFeedServiceFactory> _logger;

    public DataFeedServiceFactory(
        IServiceProvider serviceProvider,
        ISettingsRepository settingsRepo,
        IOptions<AlpacaSettings> alpacaSettings,
        ILogger<DataFeedServiceFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _settingsRepo = settingsRepo;
        _alpacaSettings = alpacaSettings.Value;
        _logger = logger;
    }

    public async Task<IDataFeedService> GetServiceAsync(CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetAsync(ct);
        return GetService(settings.PreferredDataSource);
    }

    public IDataFeedService GetService(DataSource dataSource)
    {
        if (dataSource == DataSource.Alpaca && !_alpacaSettings.HasConfiguredCredentials)
        {
            _logger.LogWarning(
                "Alpaca credentials are not configured. Falling back to Yahoo Finance data feed.");
            return _serviceProvider.GetRequiredKeyedService<IDataFeedService>(DataSource.Yahoo);
        }

        return dataSource switch
        {
            DataSource.Alpaca => _serviceProvider.GetRequiredKeyedService<IDataFeedService>(DataSource.Alpaca),
            DataSource.Yahoo => _serviceProvider.GetRequiredKeyedService<IDataFeedService>(DataSource.Yahoo),
            DataSource.LsSecurities => _serviceProvider.GetRequiredKeyedService<IDataFeedService>(DataSource.LsSecurities),
            _ => _serviceProvider.GetRequiredKeyedService<IDataFeedService>(DataSource.Alpaca)
        };
    }
}

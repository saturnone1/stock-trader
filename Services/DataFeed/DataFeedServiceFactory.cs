using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models.Enums;

namespace StockTrader.Services.DataFeed;

public interface IDataFeedServiceFactory
{
    Task<IDataFeedService> GetServiceAsync(CancellationToken ct = default);
    IDataFeedService GetService(DataSource dataSource);
    Task<DataFeedSelection> SelectAsync(
        DataSource? requestedSource,
        CancellationToken ct = default);
}

public sealed record DataFeedSelection(
    DataSource Source,
    IDataFeedService Service);

public class DataFeedServiceFactory : IDataFeedServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISettingsRepository _settingsRepo;
    private readonly AlpacaSettings _alpacaSettings;
    private readonly MarketDataTransportOptions _transport;
    private readonly ILogger<DataFeedServiceFactory> _logger;

    public DataFeedServiceFactory(
        IServiceProvider serviceProvider,
        ISettingsRepository settingsRepo,
        IOptions<AlpacaSettings> alpacaSettings,
        IOptions<MarketDataTransportOptions> transport,
        ILogger<DataFeedServiceFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _settingsRepo = settingsRepo;
        _alpacaSettings = alpacaSettings.Value;
        _transport = transport.Value;
        _logger = logger;
    }

    public async Task<IDataFeedService> GetServiceAsync(CancellationToken ct = default) =>
        (await SelectAsync(null, ct)).Service;

    public async Task<DataFeedSelection> SelectAsync(
        DataSource? requestedSource,
        CancellationToken ct = default)
    {
        var source = requestedSource
            ?? (await _settingsRepo.GetAsync(ct)).PreferredDataSource;
        var resolvedSource = _transport.Mode != MarketDataTransportMode.Remote
                             && source == DataSource.Alpaca
                             && !_alpacaSettings.HasConfiguredCredentials
            ? DataSource.Yahoo
            : source;
        return new DataFeedSelection(resolvedSource, GetService(resolvedSource));
    }

    public IDataFeedService GetService(DataSource dataSource)
    {
        if (_transport.Mode != MarketDataTransportMode.Remote
            && dataSource == DataSource.Alpaca && !_alpacaSettings.HasConfiguredCredentials)
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

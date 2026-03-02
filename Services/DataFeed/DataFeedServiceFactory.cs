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

    public DataFeedServiceFactory(
        IServiceProvider serviceProvider,
        ISettingsRepository settingsRepo)
    {
        _serviceProvider = serviceProvider;
        _settingsRepo = settingsRepo;
    }

    public async Task<IDataFeedService> GetServiceAsync(CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetAsync(ct);
        return GetService(settings.PreferredDataSource);
    }

    public IDataFeedService GetService(DataSource dataSource) => dataSource switch
    {
        DataSource.Alpaca => _serviceProvider.GetRequiredKeyedService<IDataFeedService>(DataSource.Alpaca),
        DataSource.Yahoo => _serviceProvider.GetRequiredKeyedService<IDataFeedService>(DataSource.Yahoo),
        DataSource.LsSecurities => _serviceProvider.GetRequiredKeyedService<IDataFeedService>(DataSource.LsSecurities),
        _ => _serviceProvider.GetRequiredKeyedService<IDataFeedService>(DataSource.Alpaca)
    };
}

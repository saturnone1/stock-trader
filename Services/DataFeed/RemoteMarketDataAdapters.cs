using Microsoft.Extensions.Options;
using StockTrader.Application.MarketData;
using StockTrader.Application.Settings;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.ServiceContracts.MarketData;

namespace StockTrader.Services.DataFeed;

public sealed class RemoteOhlcvRepository(
    MarketDataServiceClient client,
    ISettingsRepository settings) : IOhlcvRepository
{
    private async Task<DataSource> ProviderAsync(CancellationToken ct) =>
        (await settings.GetAsync(ct)).PreferredDataSource;

    public async Task<List<OhlcvBar>> GetBarsAsync(
        string symbol, TimeFrame timeFrame, DateTime from, DateTime to,
        CancellationToken ct = default)
    {
        var provider = await ProviderAsync(ct);
        var response = await client.ReadRangeAsync(
            MarketDataContractMapper.Range(provider, symbol, timeFrame, from, to), ct);
        return response.Bars.Select(MarketDataContractMapper.ToModel).ToList();
    }

    public async Task<OhlcvBar?> GetLatestBarAsync(
        string symbol, TimeFrame timeFrame, CancellationToken ct = default)
    {
        var provider = await ProviderAsync(ct);
        var request = MarketDataContractMapper.Range(
            provider, symbol, timeFrame, DateTime.UnixEpoch, DateTime.UtcNow.AddDays(1));
        var bar = await client.ReadLatestAsync(request, ct);
        return bar is null ? null : MarketDataContractMapper.ToModel(bar);
    }

    public async Task AddBarsAsync(IEnumerable<OhlcvBar> bars, CancellationToken ct = default)
    {
        var values = bars.ToArray();
        if (values.Length == 0) return;
        var provider = await ProviderAsync(ct);
        await new RemoteMarketDataBarWriter(client).WriteAsync(
            new MarketDataBarWrite(provider, values), ct);
    }

    public async Task<DateTime?> GetLastTimestampAsync(
        string symbol, TimeFrame timeFrame, CancellationToken ct = default) =>
        (await GetLatestBarAsync(symbol, timeFrame, ct))?.Timestamp;
}

public sealed class MarketDataRepositoryRouter(
    OhlcvRepository local,
    RemoteOhlcvRepository remote,
    IMarketDataBarWriter writer,
    ISettingsRepository settings,
    IOptions<MarketDataTransportOptions> options) : IOhlcvRepository
{
    private IOhlcvRepository Reader => options.Value.Mode == MarketDataTransportMode.Remote
        ? remote
        : local;

    public Task<List<OhlcvBar>> GetBarsAsync(string symbol, TimeFrame timeFrame, DateTime from,
        DateTime to, CancellationToken ct = default) =>
        Reader.GetBarsAsync(symbol, timeFrame, from, to, ct);

    public Task<OhlcvBar?> GetLatestBarAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default) => Reader.GetLatestBarAsync(symbol, timeFrame, ct);

    public Task<DateTime?> GetLastTimestampAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default) => Reader.GetLastTimestampAsync(symbol, timeFrame, ct);

    public async Task AddBarsAsync(IEnumerable<OhlcvBar> bars, CancellationToken ct = default)
    {
        var values = bars.ToArray();
        if (values.Length == 0) return;
        // Compatibility only. New ingestion paths use IMarketDataBarWriter with explicit provider.
        var provider = (await settings.GetAsync(ct)).PreferredDataSource;
        await writer.WriteAsync(new MarketDataBarWrite(provider, values), ct);
    }
}

public sealed class RemoteDataFeedService(
    DataSource source,
    MarketDataServiceClient client) : IDataFeedService
{
    public DataSource Source => source;

    public async Task<List<OhlcvBar>> GetHistoricalBarsAsync(
        string symbol, TimeFrame timeFrame, DateTime from, DateTime to,
        CancellationToken ct = default)
    {
        var response = await client.HistoricalAsync(new MarketDataProviderRequest(
            MarketDataContractVersions.Current, source.ToString(), symbol, timeFrame.ToString(),
            MarketDataContractHash.Utc(from), MarketDataContractHash.Utc(to), Persist: true), ct);
        if (!response.Evidence.IsComplete)
            throw new InvalidDataException(
                $"Provider returned incomplete Market Data evidence {response.Evidence.EvidenceId}.");
        return response.Bars.Select(MarketDataContractMapper.ToModel).ToList();
    }

    public async Task<OhlcvBar?> GetLatestBarAsync(
        string symbol, TimeFrame timeFrame, CancellationToken ct = default)
    {
        var response = await client.LatestAsync(new MarketDataProviderRequest(
            MarketDataContractVersions.Current, source.ToString(), symbol, timeFrame.ToString(),
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, Persist: true), ct);
        if (!response.Evidence.IsComplete)
            throw new InvalidDataException(
                $"Provider returned incomplete Market Data evidence {response.Evidence.EvidenceId}.");
        var bar = response.Bars.LastOrDefault();
        return bar is null ? null : MarketDataContractMapper.ToModel(bar);
    }

    public async Task<List<OhlcvBar>> GetIntradayBarsAsync(
        string symbol, DateTime date, CancellationToken ct = default)
    {
        var response = await client.IntradayAsync(new MarketDataIntradayRequest(
            MarketDataContractVersions.Current, source.ToString(), symbol,
            DateOnly.FromDateTime(date), Persist: true), ct);
        if (!response.Evidence.IsComplete)
            throw new InvalidDataException(
                $"Provider returned incomplete Market Data evidence {response.Evidence.EvidenceId}.");
        return response.Bars.Select(MarketDataContractMapper.ToModel).ToList();
    }

    public async Task<decimal> GetCurrentPriceAsync(
        string symbol, CancellationToken ct = default) =>
        (await client.PriceAsync(new MarketDataPriceRequest(
            MarketDataContractVersions.Current, source.ToString(), symbol), ct)).Price;
}

public sealed class MarketDataFeedRouter(
    DataSource source,
    IDataFeedService local,
    MarketDataServiceClient client,
    IOptions<MarketDataTransportOptions> options) : IDataFeedService
{
    private readonly RemoteDataFeedService _remote = new(source, client);
    public DataSource Source => source;
    private IDataFeedService Authority => options.Value.Mode == MarketDataTransportMode.Remote
        ? _remote
        : local;

    public Task<List<OhlcvBar>> GetHistoricalBarsAsync(string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct = default) =>
        Authority.GetHistoricalBarsAsync(symbol, timeFrame, from, to, ct);
    public Task<OhlcvBar?> GetLatestBarAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default) => Authority.GetLatestBarAsync(symbol, timeFrame, ct);
    public Task<List<OhlcvBar>> GetIntradayBarsAsync(string symbol, DateTime date,
        CancellationToken ct = default) => Authority.GetIntradayBarsAsync(symbol, date, ct);
    public Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken ct = default) =>
        Authority.GetCurrentPriceAsync(symbol, ct);
}

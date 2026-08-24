using StockTrader.Application.Trading;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.ServiceContracts.MarketData;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;

namespace StockTrader.Services.DataFeed;

/// <summary>현재 데이터 공급자 선택과 SQLite 일봉 조회를 연결하는 어댑터입니다.</summary>
public sealed class LiveDailyScanData(
    IDataFeedServiceFactory dataFeeds,
    IOhlcvRepository bars,
    ISettingsRepository settings,
    MarketDataServiceClient client,
    IOptions<MarketDataTransportOptions> transport) : ILiveDailyScanData
{
    public async Task<LiveDailyScanContext> ResolveContextAsync(
        CancellationToken ct = default)
    {
        var selection = await dataFeeds.SelectAsync(null, ct);
        var provider = DataProviderCatalog.Get(selection.Source);
        return new LiveDailyScanContext(
            selection.Source,
            provider.MarketRegion,
            DataProviderCatalog.RegimeBenchmarkSymbol(selection.Source));
    }

    public async Task<LiveDailyBarSet> LoadBarsAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var provider = (await settings.GetAsync(ct)).PreferredDataSource;
        var request = MarketDataContractMapper.Range(
            provider, symbol, TimeFrame.Daily, from, to);
        if (transport.Value.Mode == MarketDataTransportMode.Remote)
        {
            var response = await client.ReadRangeAsync(request, ct);
            return new LiveDailyBarSet(
                response.Bars.Select(MarketDataContractMapper.ToModel).ToArray(),
                response.Evidence);
        }

        var loaded = await bars.GetBarsAsync(symbol, TimeFrame.Daily, from, to, ct);
        var contractBars = loaded.Select(MarketDataContractMapper.ToContract).ToArray();
        var contentHash = MarketDataContractHash.Content(contractBars);
        const long localRevision = 0;
        var evidence = new MarketDataEvidenceContract(
            MarketDataContractVersions.Current,
            MarketDataContractHash.Evidence(request.Provider, request.Symbol, request.TimeFrame,
                request.AdjustmentMode, request.CalendarVersion, localRevision, contentHash),
            request.Provider, request.Symbol, request.TimeFrame, request.AdjustmentMode,
            request.Market, request.CalendarVersion,
            MarketDataContractHash.Utc(from), MarketDataContractHash.Utc(to),
            contractBars.FirstOrDefault()?.TimestampUtc,
            contractBars.LastOrDefault()?.TimestampUtc,
            localRevision, true, contentHash);
        return new LiveDailyBarSet(loaded, evidence);
    }
}

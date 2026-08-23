using StockTrader.Application.MachineLearning;
using StockTrader.Domain.MarketData;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.ServiceContracts.MarketData;

namespace StockTrader.Services.ML;

/// <summary>현재 선택된 공급자를 하나의 레짐 학습 데이터 집합으로 투영하는 어댑터입니다.</summary>
internal sealed class MarketRegimeTrainingDataSource(
    IDataFeedServiceFactory dataFeeds) : IMarketRegimeTrainingDataSource
{
    public async Task<MarketRegimeTrainingSet> LoadAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var selection = await dataFeeds.SelectAsync(null, ct);
        var symbol = DataProviderCatalog.RegimeBenchmarkSymbol(selection.Source);
        var bars = await selection.Service.GetHistoricalBarsAsync(
            symbol,
            TimeFrame.Daily,
            from,
            to,
            ct);
        var timeFrame = TimeFrame.Daily;
        var provider = selection.Source.ToString();
        var adjustment = PriceAdjustmentCatalog.Resolve(selection.Source, timeFrame).ToString();
        var calendar = MarketCalendarVersion.Current;
        var contractBars = bars.Select(bar => new MarketDataBar(
            symbol, timeFrame.ToString(), bar.Timestamp, bar.Open, bar.High,
            bar.Low, bar.Close, bar.Volume, null)).ToArray();
        var contentHash = MarketDataContractHash.Content(contractBars);
        var first = bars.Count == 0 ? from : bars.Min(bar => bar.Timestamp);
        var last = bars.Count == 0 ? to : bars.Max(bar => bar.Timestamp);
        var evidenceId = MarketDataContractHash.Evidence(
            provider, symbol, timeFrame.ToString(), adjustment, calendar, 0, contentHash);
        return new MarketRegimeTrainingSet(
            symbol, provider, timeFrame.ToString(), adjustment, calendar,
            contentHash, evidenceId, first, last, bars);
    }
}

using StockTrader.Application.MachineLearning;
using StockTrader.Domain.MarketData;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;

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
        return new MarketRegimeTrainingSet(symbol, bars);
    }
}

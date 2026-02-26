using Alpaca.Markets;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using TimeFrame = StockTrader.Models.Enums.TimeFrame;

namespace StockTrader.Services.DataFeed;

public class AlpacaDataFeedService : IDataFeedService
{
    private readonly IAlpacaDataClient _dataClient;
    private readonly ILogger<AlpacaDataFeedService> _logger;

    public AlpacaDataFeedService(IOptions<AlpacaSettings> settings,
        ILogger<AlpacaDataFeedService> logger)
    {
        _logger = logger;
        var config = settings.Value;
        var secretKey = new SecretKey(config.ApiKey, config.ApiSecret);

        _dataClient = config.IsPaper
            ? Alpaca.Markets.Environments.Paper.GetAlpacaDataClient(secretKey)
            : Alpaca.Markets.Environments.Live.GetAlpacaDataClient(secretKey);
    }

    public async Task<List<OhlcvBar>> GetHistoricalBarsAsync(string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        try
        {
            var barTimeFrame = ToAlpacaTimeFrame(timeFrame);
            var request = new HistoricalBarsRequest(symbol, from, to, barTimeFrame);

            var bars = new List<OhlcvBar>();
            var page = await _dataClient.ListHistoricalBarsAsync(request, ct);

            foreach (var bar in page.Items)
            {
                bars.Add(MapBar(symbol, timeFrame, bar));
            }

            _logger.LogInformation("Fetched {Count} {TimeFrame} bars for {Symbol} ({From:d} ~ {To:d})",
                bars.Count, timeFrame, symbol, from, to);
            return bars;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching historical bars for {Symbol}", symbol);
            return new List<OhlcvBar>();
        }
    }

    public async Task<OhlcvBar?> GetLatestBarAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default)
    {
        try
        {
            var bar = await _dataClient.GetLatestBarAsync(
                new LatestMarketDataRequest(symbol), ct);
            return MapBar(symbol, timeFrame, bar);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching latest bar for {Symbol}", symbol);
            return null;
        }
    }

    public async Task<List<OhlcvBar>> GetIntradayBarsAsync(string symbol, DateTime date,
        CancellationToken ct = default)
    {
        try
        {
            var from = date.Date.AddHours(13).AddMinutes(30); // 9:30 ET in UTC
            var to = date.Date.AddHours(20); // 4:00 PM ET in UTC
            var request = new HistoricalBarsRequest(symbol, from, to, BarTimeFrame.Minute);

            var bars = new List<OhlcvBar>();
            var page = await _dataClient.ListHistoricalBarsAsync(request, ct);

            foreach (var bar in page.Items)
            {
                bars.Add(MapBar(symbol, TimeFrame.OneMinute, bar));
            }

            _logger.LogInformation("Fetched {Count} intraday bars for {Symbol} on {Date:d}",
                bars.Count, symbol, date);
            return bars;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching intraday bars for {Symbol}", symbol);
            return new List<OhlcvBar>();
        }
    }

    public async Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken ct = default)
    {
        try
        {
            var trade = await _dataClient.GetLatestTradeAsync(
                new LatestMarketDataRequest(symbol), ct);
            return trade.Price;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching current price for {Symbol}", symbol);
            return 0m;
        }
    }

    private static BarTimeFrame ToAlpacaTimeFrame(TimeFrame timeFrame) => timeFrame switch
    {
        TimeFrame.OneMinute => BarTimeFrame.Minute,
        TimeFrame.FiveMinute => new BarTimeFrame(5, BarTimeFrameUnit.Minute),
        TimeFrame.FifteenMinute => new BarTimeFrame(15, BarTimeFrameUnit.Minute),
        TimeFrame.Daily => BarTimeFrame.Day,
        TimeFrame.Weekly => BarTimeFrame.Week,
        _ => BarTimeFrame.Day
    };

    private static OhlcvBar MapBar(string symbol, TimeFrame timeFrame, IBar bar) => new()
    {
        Symbol = symbol,
        Timestamp = bar.TimeUtc,
        TimeFrame = timeFrame,
        Open = bar.Open,
        High = bar.High,
        Low = bar.Low,
        Close = bar.Close,
        Volume = (long)bar.Volume,
        Vwap = bar.Vwap
    };
}

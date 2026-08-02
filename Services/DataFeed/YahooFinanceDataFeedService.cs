using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.DataFeed;

public class YahooFinanceDataFeedService : IDataFeedService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly YahooFinanceSettings _settings;
    private readonly ILogger<YahooFinanceDataFeedService> _logger;
    // Yahoo Finance는 동시 3개 요청까지 안정적으로 처리 가능.
    // 병렬 분석(StockAnalysisService.MaxParallelAnalyses=3)과 일치시킴.
    private readonly SemaphoreSlim _rateLimiter = new(3, 3);
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public YahooFinanceDataFeedService(
        HttpClient httpClient,
        IOptions<YahooFinanceSettings> settings,
        ILogger<YahooFinanceDataFeedService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Yahoo Finance API 분봉 기간 제한:
    ///   1m  → 최근 7일
    ///   5m  → 최근 60일
    ///   15m → 최근 60일
    ///   일봉/주봉 → 제한 없음
    /// </summary>
    private static readonly Dictionary<TimeFrame, int> IntradayMaxDays = new()
    {
        { TimeFrame.OneMinute,     7  },
        { TimeFrame.FiveMinute,    60 },
        { TimeFrame.FifteenMinute, 60 }
    };

    public async Task<List<OhlcvBar>> GetHistoricalBarsAsync(string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        try
        {
            // Yahoo Finance는 분봉 데이터에 기간 제한이 있음.
            // 요청 기간이 제한을 초과하면 API가 빈 배열을 반환하므로 자동 클램핑.
            if (IntradayMaxDays.TryGetValue(timeFrame, out var maxDays))
            {
                var earliest = to.AddDays(-maxDays);
                if (from < earliest)
                {
                    _logger.LogWarning(
                        "[Yahoo] {Symbol} {TimeFrame} 데이터: Yahoo Finance API 기간 제한 ({MaxDays}일)으로 " +
                        "시작일을 {OrigFrom:d} → {ClampedFrom:d}으로 조정합니다.",
                        symbol, timeFrame, maxDays, from, earliest);
                    from = earliest;
                }
            }

            var interval = ToYahooInterval(timeFrame);
            var period1 = new DateTimeOffset(DateTime.SpecifyKind(from, DateTimeKind.Local)).ToUnixTimeSeconds();
            var period2 = new DateTimeOffset(DateTime.SpecifyKind(to, DateTimeKind.Local)).ToUnixTimeSeconds();

            var url = $"/v8/finance/chart/{symbol}?period1={period1}&period2={period2}&interval={interval}";
            var result = await FetchChartAsync(url, ct);
            var bars = ParseBars(result, symbol, timeFrame);

            _logger.LogInformation(
                "[Yahoo] {Count}개 {TimeFrame} 바 수신: {Symbol} ({From:d} ~ {To:d})",
                bars.Count, timeFrame, symbol, from, to);
            return bars;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Yahoo] {Symbol} 히스토리 바 조회 실패", symbol);
            return [];
        }
    }

    public async Task<OhlcvBar?> GetLatestBarAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default)
    {
        try
        {
            var interval = ToYahooInterval(timeFrame);
            var url = $"/v8/finance/chart/{symbol}?range=1d&interval={interval}";
            var result = await FetchChartAsync(url, ct);
            var bars = ParseBars(result, symbol, timeFrame);
            return bars.Count > 0 ? bars[^1] : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Yahoo] {Symbol} 최신 바 조회 실패", symbol);
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
            var period1 = new DateTimeOffset(from).ToUnixTimeSeconds();
            var period2 = new DateTimeOffset(to).ToUnixTimeSeconds();

            var url = $"/v8/finance/chart/{symbol}?period1={period1}&period2={period2}&interval=1m";
            var result = await FetchChartAsync(url, ct);
            var bars = ParseBars(result, symbol, TimeFrame.OneMinute);

            _logger.LogInformation("[Yahoo] {Count}개 인트라데이 바 수신: {Symbol} ({Date:d})",
                bars.Count, symbol, date);
            return bars;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Yahoo] {Symbol} 인트라데이 바 조회 실패", symbol);
            return [];
        }
    }

    public async Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken ct = default)
    {
        try
        {
            var url = $"/v8/finance/chart/{symbol}?range=1d&interval=1m";
            var result = await FetchChartAsync(url, ct);

            if (result?.Chart?.Result is { Count: > 0 })
                return result.Chart.Result[0].Meta?.RegularMarketPrice ?? 0m;

            return 0m;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Yahoo] {Symbol} 현재가 조회 실패", symbol);
            return 0m;
        }
    }

    private async Task<YahooChartResponse?> FetchChartAsync(string url, CancellationToken ct)
    {
        await _rateLimiter.WaitAsync(ct);
        try
        {
            // Keyed DI에서 resolve될 때 BaseAddress가 설정되지 않을 수 있으므로 절대 URL 사용
            var requestUrl = _httpClient.BaseAddress != null
                ? url
                : $"{_settings.BaseUrl.TrimEnd('/')}{url}";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            if (_httpClient.BaseAddress == null)
            {
                request.Headers.Add("User-Agent", _settings.UserAgent);
            }

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<YahooChartResponse>(json, JsonOptions);
        }
        finally
        {
            // fire-and-forget Task.Run + CancellationToken.None 제거:
            // 앱 종료 시 ThreadPool 누수 및 Release 미보장 문제 해결.
            // ct를 전달하므로 취소 시 즉시 Release되어 세마포어 교착 방지.
            _ = ReleaseAfterDelayAsync(ct);
        }
    }

    private async Task ReleaseAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_settings.RateLimitDelayMs, ct);
        }
        catch (OperationCanceledException)
        {
            // 취소 시에도 반드시 Release — 세마포어 영구 점유 방지
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    private static List<OhlcvBar> ParseBars(YahooChartResponse? response, string symbol, TimeFrame timeFrame)
    {
        var bars = new List<OhlcvBar>();

        if (response?.Chart?.Result is not { Count: > 0 })
            return bars;

        var result = response.Chart.Result[0];
        var timestamps = result.Timestamp;
        var quote = result.Indicators?.Quote?.FirstOrDefault();

        if (timestamps == null || quote == null)
            return bars;

        for (var i = 0; i < timestamps.Count; i++)
        {
            var open = GetValue(quote.Open, i);
            var high = GetValue(quote.High, i);
            var low = GetValue(quote.Low, i);
            var close = GetValue(quote.Close, i);
            var volume = GetVolume(quote.Volume, i);

            // Yahoo sometimes returns null for individual candles (e.g., pre/post market)
            if (open == 0 || high == 0 || low == 0 || close == 0)
                continue;

            bars.Add(new OhlcvBar
            {
                Symbol = symbol,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(timestamps[i]).UtcDateTime,
                TimeFrame = timeFrame,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume,
                Vwap = null // Yahoo Finance does not provide VWAP
            });
        }

        return bars;
    }

    private static decimal GetValue(List<decimal?>? list, int index)
        => list != null && index < list.Count ? list[index] ?? 0m : 0m;

    private static long GetVolume(List<long?>? list, int index)
        => list != null && index < list.Count ? list[index] ?? 0L : 0L;

    public void Dispose()
    {
        if (_disposed) return;
        _rateLimiter.Dispose();
        _disposed = true;
    }

    private static string ToYahooInterval(TimeFrame timeFrame) => timeFrame switch
    {
        TimeFrame.OneMinute => "1m",
        TimeFrame.FiveMinute => "5m",
        TimeFrame.FifteenMinute => "15m",
        TimeFrame.Daily => "1d",
        TimeFrame.Weekly => "1wk",
        _ => "1d"
    };

    // Internal DTOs for Yahoo Finance v8 Chart API response
    private record YahooChartResponse(
        [property: JsonPropertyName("chart")] YahooChart? Chart);

    private record YahooChart(
        [property: JsonPropertyName("result")] List<YahooChartResult>? Result);

    private record YahooChartResult(
        [property: JsonPropertyName("meta")] YahooMeta? Meta,
        [property: JsonPropertyName("timestamp")] List<long>? Timestamp,
        [property: JsonPropertyName("indicators")] YahooIndicators? Indicators);

    private record YahooMeta(
        [property: JsonPropertyName("regularMarketPrice")] decimal RegularMarketPrice);

    private record YahooIndicators(
        [property: JsonPropertyName("quote")] List<YahooQuote>? Quote);

    private record YahooQuote(
        [property: JsonPropertyName("open")] List<decimal?>? Open,
        [property: JsonPropertyName("high")] List<decimal?>? High,
        [property: JsonPropertyName("low")] List<decimal?>? Low,
        [property: JsonPropertyName("close")] List<decimal?>? Close,
        [property: JsonPropertyName("volume")] List<long?>? Volume);
}

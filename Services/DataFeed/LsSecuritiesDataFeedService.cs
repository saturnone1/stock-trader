using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.DataFeed;

/// <summary>
/// LS증권 OPEN API 시세 데이터 서비스.
/// 분봉(t8412): 1분~60분, 최대 1년치 히스토리.
/// 일봉(t8413): 일/주/월봉, 연속조회로 수년치 가능.
/// 현재가(t1101): 종목별 실시간 현재가.
/// </summary>
public class LsSecuritiesDataFeedService : IDataFeedService
{
    private readonly HttpClient _http;
    private readonly LsSecuritiesSettings _settings;
    private readonly ILogger<LsSecuritiesDataFeedService> _logger;

    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public LsSecuritiesDataFeedService(
        HttpClient http,
        IOptions<LsSecuritiesSettings> settings,
        ILogger<LsSecuritiesDataFeedService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
        _http.BaseAddress = new Uri(_settings.BaseUrl);
    }

    #region Authentication

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry)
            return;

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["appkey"] = _settings.AppKey,
            ["appsecretkey"] = _settings.AppSecret,
            ["scope"] = "oob"
        });

        var response = await _http.PostAsync("/oauth2/token", content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("[LS Data] 토큰 발급 실패: {Status} {Body}", response.StatusCode, json);
            throw new InvalidOperationException($"LS증권 토큰 발급 실패: {response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(json);
        _accessToken = doc.RootElement.GetProperty("access_token").GetString();

        var kstNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"));
        var nextExpiry = kstNow.Hour < 7
            ? kstNow.Date.AddHours(7)
            : kstNow.Date.AddDays(1).AddHours(7);
        _tokenExpiry = TimeZoneInfo.ConvertTimeToUtc(nextExpiry,
            TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"))
            .AddMinutes(-5);

        _logger.LogInformation("[LS Data] 토큰 발급 성공");
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        string path, string trCd, object body,
        bool isContinuation = false, string? contKey = null,
        CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);

        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.Add("tr_cd", trCd);
        request.Headers.Add("tr_cont", isContinuation ? "Y" : "N");
        if (!string.IsNullOrEmpty(contKey))
            request.Headers.Add("tr_cont_key", contKey);

        var jsonStr = JsonSerializer.Serialize(body);
        request.Content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
        return request;
    }

    #endregion

    #region IDataFeedService

    /// <inheritdoc />
    public async Task<List<OhlcvBar>> GetHistoricalBarsAsync(
        string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        return timeFrame switch
        {
            TimeFrame.Daily or TimeFrame.Weekly => await GetDailyBarsAsync(symbol, timeFrame, from, to, ct),
            _ => await GetMinuteBarsAsync(symbol, timeFrame, from, to, ct)
        };
    }

    /// <inheritdoc />
    public async Task<OhlcvBar?> GetLatestBarAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default)
    {
        var to = DateTime.Now;
        var from = timeFrame == TimeFrame.Daily ? to.AddDays(-5) : to.AddHours(-2);
        var bars = await GetHistoricalBarsAsync(symbol, timeFrame, from, to, ct);
        return bars.Count > 0 ? bars[^1] : null;
    }

    /// <inheritdoc />
    public async Task<List<OhlcvBar>> GetIntradayBarsAsync(string symbol, DateTime date,
        CancellationToken ct = default)
    {
        return await GetMinuteBarsAsync(symbol, TimeFrame.OneMinute,
            date.Date, date.Date.AddDays(1), ct);
    }

    /// <inheritdoc />
    public async Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken ct = default)
    {
        try
        {
            var body = new Dictionary<string, object>
            {
                ["t1101InBlock"] = new Dictionary<string, object>
                {
                    ["shcode"] = symbol
                }
            };

            var request = await CreateRequestAsync("/stock/market-data", "t1101", body, ct: ct);
            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[LS Data] 현재가 조회 실패: {Symbol} {Status}", symbol, response.StatusCode);
                return 0;
            }

            using var doc = JsonDocument.Parse(json);
            var block = doc.RootElement.GetProperty("t1101OutBlock");
            return block.TryGetProperty("price", out var price) ? price.GetDecimal() : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LS Data] 현재가 조회 예외: {Symbol}", symbol);
            return 0;
        }
    }

    #endregion

    #region Private - Minute Bars (t8412)

    private async Task<List<OhlcvBar>> GetMinuteBarsAsync(
        string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct)
    {
        var ncnt = timeFrame switch
        {
            TimeFrame.OneMinute => 1,
            TimeFrame.FiveMinute => 5,
            TimeFrame.FifteenMinute => 15,
            _ => 1
        };

        var allBars = new List<OhlcvBar>();
        var contKey = "";
        var isCont = false;
        var maxPages = 50; // 안전 장치: 최대 50회 연속조회

        for (int page = 0; page < maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();

            var body = new Dictionary<string, object>
            {
                ["t8412InBlock"] = new Dictionary<string, object>
                {
                    ["shcode"] = symbol,
                    ["ncnt"] = ncnt,
                    ["qrycnt"] = 500,
                    ["sdate"] = from.ToString("yyyyMMdd"),
                    ["edate"] = to.ToString("yyyyMMdd"),
                    ["stime"] = "090000",
                    ["etime"] = "153000",
                    ["comp_yn"] = "N"
                }
            };

            var request = await CreateRequestAsync("/stock/chart", "t8412", body,
                isCont, contKey, ct);
            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[LS Data] 분봉 조회 실패: {Symbol} {Status}", symbol, response.StatusCode);
                break;
            }

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("t8412OutBlock1", out var items))
                break;

            var count = 0;
            foreach (var item in items.EnumerateArray())
            {
                var bar = ParseMinuteBar(item, symbol, timeFrame);
                if (bar != null)
                {
                    allBars.Add(bar);
                    count++;
                }
            }

            if (count == 0) break;

            // 연속조회 확인
            if (doc.RootElement.TryGetProperty("t8412OutBlock", out var header)
                && header.TryGetProperty("cts_date", out var ctsDate)
                && header.TryGetProperty("cts_time", out var ctsTime))
            {
                var nextKey = ctsDate.GetString() + ctsTime.GetString();
                if (string.IsNullOrEmpty(nextKey) || nextKey == contKey)
                    break;

                contKey = nextKey;
                isCont = true;
            }
            else
            {
                break;
            }

            // rate limit 대응
            await Task.Delay(120, ct);
        }

        // 시간 순 정렬 (LS API는 역순으로 줄 수 있음)
        allBars.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        _logger.LogInformation("[LS Data] {Symbol} {TF} 분봉 {Count}건 조회 ({From:d}~{To:d})",
            symbol, timeFrame, allBars.Count, from, to);
        return allBars;
    }

    private static OhlcvBar? ParseMinuteBar(JsonElement item, string symbol, TimeFrame tf)
    {
        var dateStr = item.TryGetProperty("date", out var d) ? d.GetString() : null;
        var timeStr = item.TryGetProperty("time", out var t) ? t.GetString() : null;

        if (string.IsNullOrEmpty(dateStr) || string.IsNullOrEmpty(timeStr))
            return null;

        if (!DateTime.TryParseExact($"{dateStr}{timeStr}",
            "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
            return null;

        return new OhlcvBar
        {
            Symbol = symbol,
            Timestamp = ts,
            TimeFrame = tf,
            Open = item.TryGetProperty("open", out var o) ? o.GetDecimal() : 0,
            High = item.TryGetProperty("high", out var h) ? h.GetDecimal() : 0,
            Low = item.TryGetProperty("low", out var l) ? l.GetDecimal() : 0,
            Close = item.TryGetProperty("close", out var c) ? c.GetDecimal() : 0,
            Volume = item.TryGetProperty("jdiff_vol", out var v) ? v.GetInt64() : 0
        };
    }

    #endregion

    #region Private - Daily Bars (t8413)

    private async Task<List<OhlcvBar>> GetDailyBarsAsync(
        string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct)
    {
        var dwmcode = timeFrame == TimeFrame.Weekly ? "2" : "1"; // 1=일, 2=주, 3=월

        var allBars = new List<OhlcvBar>();
        var contKey = "";
        var isCont = false;
        var maxPages = 30;

        for (int page = 0; page < maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();

            var body = new Dictionary<string, object>
            {
                ["t8413InBlock"] = new Dictionary<string, object>
                {
                    ["shcode"] = symbol,
                    ["dwmcode"] = dwmcode,
                    ["qrycnt"] = 500,
                    ["sdate"] = from.ToString("yyyyMMdd"),
                    ["edate"] = to.ToString("yyyyMMdd"),
                    ["comp_yn"] = "N"
                }
            };

            var request = await CreateRequestAsync("/stock/chart", "t8413", body,
                isCont, contKey, ct);
            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[LS Data] 일봉 조회 실패: {Symbol} {Status}", symbol, response.StatusCode);
                break;
            }

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("t8413OutBlock1", out var items))
                break;

            var count = 0;
            foreach (var item in items.EnumerateArray())
            {
                var bar = ParseDailyBar(item, symbol, timeFrame);
                if (bar != null)
                {
                    allBars.Add(bar);
                    count++;
                }
            }

            if (count == 0) break;

            // 연속조회
            if (doc.RootElement.TryGetProperty("t8413OutBlock", out var header)
                && header.TryGetProperty("cts_date", out var ctsDate))
            {
                var nextKey = ctsDate.GetString() ?? "";
                if (string.IsNullOrEmpty(nextKey) || nextKey == contKey)
                    break;

                contKey = nextKey;
                isCont = true;
            }
            else
            {
                break;
            }

            await Task.Delay(120, ct);
        }

        allBars.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        _logger.LogInformation("[LS Data] {Symbol} {TF} 일봉 {Count}건 조회 ({From:d}~{To:d})",
            symbol, timeFrame, allBars.Count, from, to);
        return allBars;
    }

    private static OhlcvBar? ParseDailyBar(JsonElement item, string symbol, TimeFrame tf)
    {
        var dateStr = item.TryGetProperty("date", out var d) ? d.GetString() : null;
        if (string.IsNullOrEmpty(dateStr)) return null;

        if (!DateTime.TryParseExact(dateStr, "yyyyMMdd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
            return null;

        return new OhlcvBar
        {
            Symbol = symbol,
            Timestamp = ts,
            TimeFrame = tf,
            Open = item.TryGetProperty("open", out var o) ? o.GetDecimal() : 0,
            High = item.TryGetProperty("high", out var h) ? h.GetDecimal() : 0,
            Low = item.TryGetProperty("low", out var l) ? l.GetDecimal() : 0,
            Close = item.TryGetProperty("close", out var c) ? c.GetDecimal() : 0,
            Volume = item.TryGetProperty("jdiff_vol", out var v) ? v.GetInt64() : 0
        };
    }

    #endregion
}

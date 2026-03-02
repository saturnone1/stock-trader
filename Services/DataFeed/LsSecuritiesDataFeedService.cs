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
/// 현재가(t1102): 종목별 시세(현재가, 등락, 거래량 등).
/// </summary>
public class LsSecuritiesDataFeedService : IDataFeedService
{
    private readonly HttpClient _http;
    private readonly LsSecuritiesSettings _settings;
    private readonly ILogger<LsSecuritiesDataFeedService> _logger;

    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    // LS증권 차트 TR rate limit: 초당 1건 이내
    private DateTime _lastChartRequest = DateTime.MinValue;
    private readonly SemaphoreSlim _chartRateLock = new(1, 1);

    private static readonly TimeZoneInfo KstZone = GetKstZone();
    private static TimeZoneInfo GetKstZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul"); }
    }

    public LsSecuritiesDataFeedService(
        HttpClient http,
        IOptions<LsSecuritiesSettings> settings,
        ILogger<LsSecuritiesDataFeedService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
        // LS증권: 분봉(t8412)·현재가(t1102)는 운영서버에서 작동, 일봉(t8413)은 모의서버 전용
        // → 항상 운영서버 사용 + 일봉은 분봉 집계로 대체
        _http.BaseAddress = new Uri(_settings.BaseUrl);
    }

    #region Authentication

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry)
            return;

        await _tokenLock.WaitAsync(ct);
        try
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

            var kstNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, KstZone);
            var nextExpiry = kstNow.Hour < 7
                ? kstNow.Date.AddHours(7)
                : kstNow.Date.AddDays(1).AddHours(7);
            _tokenExpiry = TimeZoneInfo.ConvertTimeToUtc(nextExpiry, KstZone)
                .AddMinutes(-5);

            _logger.LogInformation("[LS Data] 토큰 발급 성공");
        }
        finally
        {
            _tokenLock.Release();
        }
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
        var to = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, KstZone);
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
                ["t1102InBlock"] = new Dictionary<string, object>
                {
                    ["shcode"] = symbol
                }
            };

            var request = await CreateRequestAsync("/stock/market-data", "t1102", body, ct: ct);
            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[LS Data] 현재가 조회 실패: {Symbol} {Status}", symbol, response.StatusCode);
                return 0;
            }

            using var doc = JsonDocument.Parse(json);
            var block = doc.RootElement.GetProperty("t1102OutBlock");
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

        var bars = await GetMinuteBarsInternal(symbol, ncnt, from, to, ct);

        // TimeFrame 태깅
        foreach (var bar in bars)
            bar.TimeFrame = timeFrame;

        _logger.LogInformation("[LS Data] {Symbol} {TF} 분봉 {Count}건 조회 ({From:d}~{To:d})",
            symbol, timeFrame, bars.Count, from, to);
        return bars;
    }

    /// <summary>t8412 분봉 조회 공통 내부 메서드. 일봉 집계에서도 재사용.</summary>
    private async Task<List<OhlcvBar>> GetMinuteBarsInternal(
        string symbol, int ncnt,
        DateTime from, DateTime to, CancellationToken ct)
    {
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

            // rate limit: 차트 TR은 초당 1건
            await _chartRateLock.WaitAsync(ct);
            try
            {
                var elapsed = DateTime.UtcNow - _lastChartRequest;
                if (elapsed.TotalMilliseconds < 1000)
                    await Task.Delay(1000 - (int)elapsed.TotalMilliseconds, ct);
                _lastChartRequest = DateTime.UtcNow;
            }
            finally { _chartRateLock.Release(); }

            var request = await CreateRequestAsync("/stock/chart", "t8412", body,
                isCont, contKey, ct);
            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[LS Data] 분봉 조회 실패: {Symbol} {Status} {Body}",
                    symbol, response.StatusCode, json.Length > 300 ? json[..300] : json);
                break;
            }

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("t8412OutBlock1", out var items))
                break;

            var count = 0;
            foreach (var item in items.EnumerateArray())
            {
                var bar = ParseMinuteBar(item, symbol, TimeFrame.OneMinute);
                if (bar != null)
                {
                    allBars.Add(bar);
                    count++;
                }
            }

            if (count == 0) break;

            // 연속조회: body의 cts_date/cts_time이 비어있지 않아야 실제 추가 데이터 있음
            var hasMoreData = false;
            if (doc.RootElement.TryGetProperty("t8412OutBlock", out var header)
                && header.TryGetProperty("cts_date", out var ctsDate)
                && header.TryGetProperty("cts_time", out var ctsTime))
            {
                var ctsDateStr = ctsDate.GetString() ?? "";
                var ctsTimeStr = ctsTime.GetString() ?? "";
                if (!string.IsNullOrEmpty(ctsDateStr) && !string.IsNullOrEmpty(ctsTimeStr))
                {
                    // response header의 tr_cont_key를 연속조회에 사용
                    contKey = response.Headers.TryGetValues("tr_cont_key", out var keyVals)
                        ? keyVals.FirstOrDefault() ?? ""
                        : "";
                    isCont = true;
                    hasMoreData = true;
                }
            }

            if (!hasMoreData)
                break;
        }

        // 시간 순 정렬 (LS API는 역순으로 줄 수 있음)
        allBars.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
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

    #region Private - Daily Bars (분봉 집계)

    /// <summary>
    /// 일봉 데이터를 60분봉(t8412) 조회 후 날짜별 OHLCV로 집계하여 생성.
    /// t8413 일봉 TR이 운영서버에서 데이터를 반환하지 않는 제한이 있어 이 방식을 사용.
    /// 60분봉은 하루 약 7개(09:00~15:30) → 500건/페이지로 약 70일치 가능.
    /// </summary>
    private async Task<List<OhlcvBar>> GetDailyBarsAsync(
        string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct)
    {
        // 60분봉으로 조회 → 하루당 바 수가 적어 효율적
        var minuteBars = await GetMinuteBarsInternal(symbol, 60, from, to, ct);

        if (minuteBars.Count == 0)
        {
            _logger.LogWarning("[LS Data] {Symbol} 일봉 집계: 분봉 데이터 없음 ({From:d}~{To:d})", symbol, from, to);
            return [];
        }

        // 날짜별로 그룹핑하여 일봉 생성
        var dailyBars = minuteBars
            .GroupBy(b => b.Timestamp.Date)
            .Select(g => new OhlcvBar
            {
                Symbol = symbol,
                Timestamp = g.Key,
                TimeFrame = timeFrame,
                Open = g.First().Open,
                High = g.Max(b => b.High),
                Low = g.Min(b => b.Low),
                Close = g.Last().Close,
                Volume = g.Sum(b => b.Volume)
            })
            .OrderBy(b => b.Timestamp)
            .ToList();

        _logger.LogInformation("[LS Data] {Symbol} {TF} 일봉 {Count}건 집계 ({From:d}~{To:d})",
            symbol, timeFrame, dailyBars.Count, from, to);
        return dailyBars;
    }

    #endregion
}

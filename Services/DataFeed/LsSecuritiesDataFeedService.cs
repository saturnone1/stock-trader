using System.Globalization;
using System.Text.Json;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.LsSecurities;

namespace StockTrader.Services.DataFeed;

/// <summary>
/// LS증권 OPEN API 시세 데이터 서비스.
/// 분봉(t8412): 1분~60분, 최대 1년치 히스토리.
/// 일봉(t8410): 기간별 주가 조회, 수년치 일/주/월/년봉 지원.
/// 현재가(t1102): 종목별 시세(현재가, 등락, 거래량 등).
/// </summary>
public class LsSecuritiesDataFeedService : IDataFeedService
{
    private readonly HttpClient _http;
    private readonly LsAuthService _auth;
    private readonly ILogger<LsSecuritiesDataFeedService> _logger;

    public LsSecuritiesDataFeedService(
        HttpClient http,
        LsAuthService auth,
        ILogger<LsSecuritiesDataFeedService> logger)
    {
        _http = http;
        _auth = auth;
        _logger = logger;
        // LS증권: 분봉(t8412)·일봉(t8410)·현재가(t1102) 모두 운영서버 사용
        _http.BaseAddress = new Uri(_auth.Settings.BaseUrl);
    }

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
        var to = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, LsAuthService.KstZone);
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

            var request = await _auth.CreateRequestAsync(_http, "/stock/market-data", "t1102", body, ct: ct);
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
            await _auth.WaitForChartRateLimitAsync(ct);

            var request = await _auth.CreateRequestAsync(_http, "/stock/chart", "t8412", body,
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

    #region Private - Daily/Weekly Bars (t8410 기간별 주가)

    /// <summary>
    /// 일봉/주봉 데이터를 t8410(기간별 주가) TR로 직접 조회.
    /// t8410은 수년치 일/주/월/년봉 데이터를 지원하며 수정주가 옵션 포함.
    /// t8410 실패 시 60분봉(t8412) 집계로 폴백 (최대 ~1년치).
    /// </summary>
    private async Task<List<OhlcvBar>> GetDailyBarsAsync(
        string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct)
    {
        // 1차: t8410 기간별 주가 직접 조회
        var bars = await GetDailyBarsViaT8410Async(symbol, timeFrame, from, to, ct);
        if (bars.Count > 0)
            return bars;

        // 폴백: 60분봉 집계 (최대 ~1년치)
        _logger.LogWarning("[LS Data] {Symbol} t8410 조회 실패, 60분봉 집계로 폴백", symbol);
        return await GetDailyBarsViaMinuteAggregationAsync(symbol, timeFrame, from, to, ct);
    }

    /// <summary>t8410 기간별 주가 조회. gubun: 2=일, 3=주, 4=월, 5=년</summary>
    private async Task<List<OhlcvBar>> GetDailyBarsViaT8410Async(
        string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct)
    {
        var gubun = timeFrame == TimeFrame.Weekly ? "3" : "2"; // 2=일봉, 3=주봉
        var allBars = new List<OhlcvBar>();
        var contKey = "";
        var isCont = false;
        var maxPages = 20; // 500건/페이지 × 20 = 10,000일 (약 40년)

        for (int page = 0; page < maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();

            var body = new Dictionary<string, object>
            {
                ["t8410InBlock"] = new Dictionary<string, object>
                {
                    ["shcode"] = symbol,
                    ["gubun"] = gubun,
                    ["qrycnt"] = 500,
                    ["sdate"] = from.ToString("yyyyMMdd"),
                    ["edate"] = to.ToString("yyyyMMdd"),
                    ["cts_date"] = "",
                    ["comp_yn"] = "N",
                    ["sujung"] = "Y"
                }
            };

            await _auth.WaitForChartRateLimitAsync(ct);

            var request = await _auth.CreateRequestAsync(_http, "/stock/chart", "t8410", body,
                isCont, contKey, ct);
            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[LS Data] t8410 일봉 조회 실패: {Symbol} {Status} {Body}",
                    symbol, response.StatusCode, json.Length > 300 ? json[..300] : json);
                break;
            }

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("t8410OutBlock1", out var items))
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

            // 연속조회: cts_date가 비어있지 않으면 추가 데이터 존재
            var hasMore = false;
            if (doc.RootElement.TryGetProperty("t8410OutBlock", out var header)
                && header.TryGetProperty("cts_date", out var ctsDate))
            {
                var ctsDateStr = ctsDate.GetString() ?? "";
                if (!string.IsNullOrEmpty(ctsDateStr))
                {
                    contKey = response.Headers.TryGetValues("tr_cont_key", out var keyVals)
                        ? keyVals.FirstOrDefault() ?? ""
                        : "";
                    isCont = true;
                    hasMore = true;
                }
            }

            if (!hasMore) break;
        }

        allBars.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        _logger.LogInformation("[LS Data] {Symbol} t8410 일봉 {Count}건 조회 ({From:d}~{To:d})",
            symbol, allBars.Count, from, to);

        return allBars;
    }

    private static OhlcvBar? ParseDailyBar(JsonElement item, string symbol, TimeFrame tf)
    {
        var dateStr = item.TryGetProperty("date", out var d) ? d.GetString() : null;
        if (string.IsNullOrEmpty(dateStr)) return null;

        if (!DateTime.TryParseExact(dateStr, "yyyyMMdd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
            return null;

        // t8410은 volume 또는 jdiff_vol 필드를 사용
        long volume = 0;
        if (item.TryGetProperty("jdiff_vol", out var jv))
            volume = jv.GetInt64();
        else if (item.TryGetProperty("volume", out var vol))
            volume = vol.GetInt64();

        return new OhlcvBar
        {
            Symbol = symbol,
            Timestamp = ts,
            TimeFrame = tf,
            Open = item.TryGetProperty("open", out var o) ? o.GetDecimal() : 0,
            High = item.TryGetProperty("high", out var h) ? h.GetDecimal() : 0,
            Low = item.TryGetProperty("low", out var l) ? l.GetDecimal() : 0,
            Close = item.TryGetProperty("close", out var c) ? c.GetDecimal() : 0,
            Volume = volume
        };
    }

    /// <summary>폴백: 60분봉(t8412)을 날짜별 집계하여 일봉 생성 (최대 ~1년치)</summary>
    private async Task<List<OhlcvBar>> GetDailyBarsViaMinuteAggregationAsync(
        string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct)
    {
        var minuteBars = await GetMinuteBarsInternal(symbol, 60, from, to, ct);

        if (minuteBars.Count == 0)
        {
            _logger.LogWarning("[LS Data] {Symbol} 일봉 폴백 집계: 분봉 데이터도 없음 ({From:d}~{To:d})",
                symbol, from, to);
            return [];
        }

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

        _logger.LogInformation("[LS Data] {Symbol} 일봉 폴백 집계 {Count}건 ({From:d}~{To:d})",
            symbol, dailyBars.Count, from, to);
        return dailyBars;
    }

    #endregion
}

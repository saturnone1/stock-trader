using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;

namespace StockTrader.Services.LsSecurities;

/// <summary>
/// LS증권 OPEN API 공통 인증 레이어.
/// OAuth2 토큰 발급/갱신, 요청 생성, KST 타임존, 차트 TR 레이트 리미터를 통합 관리.
/// BrokerService와 DataFeedService 양쪽에서 공유.
/// </summary>
public class LsAuthService
{
    private readonly LsSecuritiesSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _tokenExpirySafetyMargin;
    private readonly ILogger<LsAuthService> _logger;

    private string? _accessToken;
    private DateTimeOffset _tokenExpiryUtc = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    // LS증권 차트 TR rate limit: 초당 1건 이내
    private long? _lastChartRequestTimestamp;
    private readonly SemaphoreSlim _chartRateLock = new(1, 1);

    public static readonly TimeZoneInfo KstZone = GetKstZone();

    public LsSecuritiesSettings Settings => _settings;

    public LsAuthService(
        IOptions<LsSecuritiesSettings> settings,
        TimeProvider timeProvider,
        ILogger<LsAuthService> logger)
    {
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _tokenExpirySafetyMargin = TimeSpan.FromMinutes(
            _settings.TokenExpirySafetyMinutes);
        _logger = logger;
    }

    /// <summary>Windows("Korea Standard Time") / Linux("Asia/Seoul") 양쪽 호환</summary>
    private static TimeZoneInfo GetKstZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul"); }
    }

    /// <summary>
    /// OAuth2 access_token 발급. 만료 시 자동 재발급. SemaphoreSlim으로 레이스 컨디션 방지.
    /// </summary>
    public async Task EnsureTokenAsync(HttpClient http, CancellationToken ct)
    {
        if (_accessToken != null && _timeProvider.GetUtcNow() < _tokenExpiryUtc)
            return;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken != null && _timeProvider.GetUtcNow() < _tokenExpiryUtc)
                return;

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["appkey"] = _settings.AppKey,
                ["appsecretkey"] = _settings.AppSecret,
                ["scope"] = "oob"
            });

            var response = await http.PostAsync("/oauth2/token", content, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[LS Auth] 토큰 발급 실패: {Status} {Body}", response.StatusCode, json);
                throw new InvalidOperationException($"LS증권 토큰 발급 실패: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(json);
            var token = doc.RootElement.GetProperty("access_token").GetString();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("[LS Auth] 토큰 응답에 access_token이 비어있음: {Body}", json);
                throw new InvalidOperationException("LS증권 토큰 응답에 access_token이 비어있습니다.");
            }
            _accessToken = token;

            _tokenExpiryUtc = LsOperationalTimingPolicy.CalculateTokenExpiryUtc(
                _timeProvider.GetUtcNow(),
                KstZone,
                LsOperationalTimingPolicy.DailyTokenExpiryKst,
                _tokenExpirySafetyMargin);

            _logger.LogInformation(
                "[LS Auth] 토큰 발급 성공, 안전 갱신 시각: {Expiry:u}",
                _tokenExpiryUtc);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>공통 헤더를 설정한 HttpRequestMessage 생성 (POST)</summary>
    public async Task<HttpRequestMessage> CreateRequestAsync(
        HttpClient http, string path, string trCd, object body,
        bool isContinuation = false, string? contKey = null,
        CancellationToken ct = default)
    {
        return await CreateRequestAsync(http, HttpMethod.Post, path, trCd, body,
            isContinuation, contKey, ct);
    }

    /// <summary>공통 헤더를 설정한 HttpRequestMessage 생성 (임의 메서드)</summary>
    public async Task<HttpRequestMessage> CreateRequestAsync(
        HttpClient http, HttpMethod method, string path, string trCd,
        object? body = null, bool isContinuation = false, string? contKey = null,
        CancellationToken ct = default)
    {
        await EnsureTokenAsync(http, ct);

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.Add("tr_cd", trCd);
        request.Headers.Add("tr_cont", isContinuation ? "Y" : "N");
        if (!string.IsNullOrEmpty(contKey))
            request.Headers.Add("tr_cont_key", contKey);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    /// <summary>
    /// 차트 TR 요청 전 rate limit 대기. LS증권 차트 TR은 초당 1건 제한.
    /// </summary>
    public async Task WaitForChartRateLimitAsync(CancellationToken ct)
    {
        await _chartRateLock.WaitAsync(ct);
        try
        {
            var currentTimestamp = _timeProvider.GetTimestamp();
            var elapsed = _lastChartRequestTimestamp.HasValue
                ? _timeProvider.GetElapsedTime(
                    _lastChartRequestTimestamp.Value,
                    currentTimestamp)
                : (TimeSpan?)null;
            var delay = LsOperationalTimingPolicy.CalculateRateLimitDelay(
                elapsed,
                LsOperationalTimingPolicy.MinimumChartRequestInterval);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, _timeProvider, ct);
            _lastChartRequestTimestamp = _timeProvider.GetTimestamp();
        }
        finally { _chartRateLock.Release(); }
    }
}

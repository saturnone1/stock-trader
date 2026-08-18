namespace StockTrader.Configuration;

/// <summary>
/// LS증권 OPEN API 설정 — appsettings.json의 "LsSecurities" 섹션에 바인딩.
/// </summary>
public class LsSecuritiesSettings
{
    /// <summary>앱 키 (LS증권 개발자센터에서 발급)</summary>
    public string AppKey { get; set; } = string.Empty;

    /// <summary>앱 시크릿 키</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>계좌번호 (앞 8자리)</summary>
    public string AccountNo { get; set; } = string.Empty;

    /// <summary>계좌 비밀번호 (4자리)</summary>
    public string AccountPassword { get; set; } = string.Empty;

    /// <summary>모의투자 여부 (true=모의, false=실전)</summary>
    public bool IsPaper { get; set; } = true;

    /// <summary>REST API 운영 URL</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>REST API 모의투자 URL</summary>
    public string PaperBaseUrl { get; set; } = string.Empty;

    /// <summary>WebSocket URL (운영)</summary>
    public string WebSocketUrl { get; set; } = string.Empty;

    /// <summary>WebSocket URL (모의투자)</summary>
    public string WebSocketPaperUrl { get; set; } = string.Empty;

    /// <summary>토큰 실제 만료 전에 갱신하기 위한 안전 여유(분)</summary>
    public int TokenExpirySafetyMinutes { get; set; }

    /// <summary>실제 사용할 REST URL (IsPaper 기반 자동 분기)</summary>
    public string EffectiveBaseUrl => IsPaper ? PaperBaseUrl : BaseUrl;

    /// <summary>실제 사용할 WebSocket URL</summary>
    public string EffectiveWebSocketUrl => IsPaper ? WebSocketPaperUrl : WebSocketUrl;
}

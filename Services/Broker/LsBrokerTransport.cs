using System.Net;
using StockTrader.Services.LsSecurities;

namespace StockTrader.Services.Broker;

internal sealed record LsBrokerHttpResponse(HttpStatusCode StatusCode, string Body)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
}

/// <summary>LS 인증 요청 생성과 HTTP 응답 수명만 소유하는 공통 전송 어댑터입니다.</summary>
internal sealed class LsBrokerTransport(HttpClient http, LsAuthService auth)
{
    public async Task<LsBrokerHttpResponse> PostAsync(
        string path,
        string transactionCode,
        object body,
        CancellationToken ct)
    {
        using var request = await auth.CreateRequestAsync(
            http,
            HttpMethod.Post,
            path,
            transactionCode,
            body,
            ct: ct);
        using var response = await http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        return new LsBrokerHttpResponse(response.StatusCode, json);
    }
}

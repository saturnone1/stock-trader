using System.Text.Json;
using System.Text.Json.Serialization;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.LsSecurities;

namespace StockTrader.Services.Broker;

/// <summary>
/// LS증권 OPEN API 브로커 어댑터.
/// REST API 기반 국내주식 매매 (매수/매도/취소, 잔고 조회, 체결 내역).
/// 공식 문서: https://openapi.ls-sec.co.kr
/// </summary>
public class LsSecuritiesBrokerService : IBrokerService
{
    private readonly HttpClient _http;
    private readonly LsAuthService _auth;
    private readonly ILogger<LsSecuritiesBrokerService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null, // LS API는 PascalCase/특정 키 사용
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public LsSecuritiesBrokerService(
        HttpClient http,
        LsAuthService auth,
        ILogger<LsSecuritiesBrokerService> logger)
    {
        _http = http;
        _auth = auth;
        _logger = logger;
        _http.BaseAddress = new Uri(_auth.Settings.EffectiveBaseUrl);
    }

    #region IBrokerService

    /// <inheritdoc />
    public async Task<bool> PlaceOrderAsync(TradeRecommendation recommendation, CancellationToken ct = default)
    {
        try
        {
            // BnsTpCode: 2=매수, 1=매도 (recommendation은 항상 매수 진입)
            var orderBody = new Dictionary<string, object>
            {
                ["CSPAT00600InBlock1"] = new Dictionary<string, object>
                {
                    ["AcntNo"] = _auth.Settings.AccountNo,
                    ["InptPwd"] = _auth.Settings.AccountPassword,
                    ["IsuNo"] = $"A{recommendation.Symbol}", // LS 종목코드: A + 6자리
                    ["OrdQty"] = recommendation.ShareQuantity,
                    ["OrdPrc"] = recommendation.EntryPrice,
                    ["BnsTpCode"] = "2", // 매수
                    ["OrdprcPtnCode"] = "00", // 지정가
                    ["MgntrnCode"] = "000", // 현금
                    ["LoanDt"] = "",
                    ["OrdCndiTpCode"] = "0"
                }
            };

            var request = await _auth.CreateRequestAsync(_http, HttpMethod.Post, "/stock/order",
                "CSPAT00600", orderBody, ct: ct);
            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[LS] 매수 주문 실패: {Status} {Body}", response.StatusCode, json);
                return false;
            }

            _logger.LogInformation("[LS] 매수 주문 성공: {Symbol} {Qty}주 @ {Price}",
                recommendation.Symbol, recommendation.ShareQuantity, recommendation.EntryPrice);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LS] 매수 주문 중 예외: {Symbol}", recommendation.Symbol);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ClosePositionAsync(string symbol, CancellationToken ct = default)
    {
        try
        {
            // 보유 잔고 조회 후 전량 시장가 매도
            var positions = await GetPositionsAsync(ct);
            var pos = positions.FirstOrDefault(p => p.Symbol == symbol
                || p.Symbol == $"A{symbol}");

            if (pos == null || pos.Quantity <= 0)
            {
                _logger.LogWarning("[LS] 청산할 포지션 없음: {Symbol}", symbol);
                return false;
            }

            var sellBody = new Dictionary<string, object>
            {
                ["CSPAT00600InBlock1"] = new Dictionary<string, object>
                {
                    ["AcntNo"] = _auth.Settings.AccountNo,
                    ["InptPwd"] = _auth.Settings.AccountPassword,
                    ["IsuNo"] = $"A{symbol}",
                    ["OrdQty"] = pos.Quantity,
                    ["OrdPrc"] = 0,       // 시장가
                    ["BnsTpCode"] = "1",  // 매도
                    ["OrdprcPtnCode"] = "03", // 시장가
                    ["MgntrnCode"] = "000",
                    ["LoanDt"] = "",
                    ["OrdCndiTpCode"] = "0"
                }
            };

            var request = await _auth.CreateRequestAsync(_http, HttpMethod.Post, "/stock/order",
                "CSPAT00600", sellBody, ct: ct);
            var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("[LS] 포지션 청산 실패: {Symbol} {Status} {Body}",
                    symbol, response.StatusCode, body);
                return false;
            }

            _logger.LogInformation("[LS] 포지션 청산 성공: {Symbol} {Qty}주",
                symbol, pos.Quantity);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LS] 포지션 청산 중 예외: {Symbol}", symbol);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        try
        {
            if (!long.TryParse(orderId, out var orderNo))
            {
                _logger.LogWarning("[LS] 잘못된 주문번호 형식: {OrderId}", orderId);
                return false;
            }

            var cancelBody = new Dictionary<string, object>
            {
                ["CSPAT00800InBlock1"] = new Dictionary<string, object>
                {
                    ["AcntNo"] = _auth.Settings.AccountNo,
                    ["InptPwd"] = _auth.Settings.AccountPassword,
                    ["OrgOrdNo"] = orderNo,
                    ["IsuNo"] = "",
                    ["OrdQty"] = 0 // 전량 취소
                }
            };

            var request = await _auth.CreateRequestAsync(_http, HttpMethod.Post, "/stock/order",
                "CSPAT00800", cancelBody, ct: ct);
            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[LS] 주문 취소 실패: {OrderId} {Status} {Body}",
                    orderId, response.StatusCode, json);
                return false;
            }

            _logger.LogInformation("[LS] 주문 취소 성공: {OrderId}", orderId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LS] 주문 취소 중 예외: {OrderId}", orderId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<List<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        try
        {
            var body = new Dictionary<string, object>
            {
                ["t0424InBlock"] = new Dictionary<string, object>
                {
                    ["pession"] = "0",
                    ["cts_expcode"] = "",
                    ["cts_medession"] = "0"
                }
            };

            var request = await _auth.CreateRequestAsync(_http, HttpMethod.Post, "/stock/accno",
                "t0424", body, ct: ct);
            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[LS] 잔고 조회 실패: {Status} {Body}", response.StatusCode, json);
                return [];
            }

            using var doc = JsonDocument.Parse(json);
            var positions = new List<Position>();

            if (doc.RootElement.TryGetProperty("t0424OutBlock1", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var symbol = item.GetProperty("expcode").GetString() ?? "";
                    var qty = item.TryGetProperty("janqty", out var jq) ? jq.GetInt64() : 0;
                    var avgPrice = item.TryGetProperty("pamt", out var pa) ? pa.GetDecimal() : 0;
                    var curPrice = item.TryGetProperty("price", out var pr) ? pr.GetDecimal() : 0;

                    if (qty <= 0) continue;

                    positions.Add(new Position
                    {
                        Symbol = symbol,
                        Quantity = (int)qty,
                        EntryPrice = avgPrice,
                        CurrentPrice = curPrice,
                        OpenedAt = DateTime.UtcNow
                    });
                }
            }

            _logger.LogInformation("[LS] 보유 종목 {Count}건 조회", positions.Count);
            return positions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LS] 보유 종목 조회 중 예외");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<BrokerAccount?> GetAccountAsync(CancellationToken ct = default)
    {
        try
        {
            var body = new Dictionary<string, object>
            {
                ["CSPAQ12300InBlock1"] = new Dictionary<string, object>
                {
                    ["RecCnt"] = 1,
                    ["BalCreTp"] = "0",
                    ["CmsnAppTpCode"] = "0",
                    ["D2balBaseQryTp"] = "0",
                    ["UprcTpCode"] = "0"
                }
            };

            var request = await _auth.CreateRequestAsync(_http, HttpMethod.Post, "/stock/accno",
                "CSPAQ12300", body, ct: ct);
            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[LS] 계좌 조회 실패: {Status} {Body}", response.StatusCode, json);
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("CSPAQ12300OutBlock2", out var block2))
            {
                _logger.LogWarning("[LS] 계좌 응답에 OutBlock2 없음: {Body}", json);
                return null;
            }

            return new BrokerAccount
            {
                AccountId = _auth.Settings.AccountNo,
                TotalEquity = block2.TryGetProperty("DpsastTotamt", out var te) ? te.GetDecimal() : 0,
                Cash = block2.TryGetProperty("D2Dps", out var cash) ? cash.GetDecimal() : 0,
                BuyingPower = block2.TryGetProperty("MnyOrdAbleAmt", out var bp) ? bp.GetDecimal() : 0,
                UnrealizedPnL = block2.TryGetProperty("InvstOrgAmt", out var upl) ? upl.GetDecimal() : 0,
                FetchedAt = DateTime.UtcNow,
                StatusMessage = "정상"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LS] 계좌 조회 중 예외");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<List<BrokerOrder>> GetOrderHistoryAsync(DateTime from, DateTime to,
        CancellationToken ct = default)
    {
        try
        {
            // CSPAQ13700은 단일 일자(OrdDt) 조회 TR — from~to 범위를 순차 조회
            var allOrders = new List<BrokerOrder>();
            for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
            {
                var dayOrders = await GetOrdersForDateAsync(date, ct);
                allOrders.AddRange(dayOrders);
            }
            _logger.LogInformation("[LS] 주문 내역 {Count}건 조회 ({From:d}~{To:d})",
                allOrders.Count, from, to);
            return allOrders;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LS] 체결내역 조회 중 예외");
            return [];
        }
    }

    private async Task<List<BrokerOrder>> GetOrdersForDateAsync(DateTime date, CancellationToken ct)
    {
        try
        {
            var body = new Dictionary<string, object>
            {
                ["CSPAQ13700InBlock1"] = new Dictionary<string, object>
                {
                    ["RecCnt"] = 300,
                    ["AcntNo"] = _auth.Settings.AccountNo,
                    ["InptPwd"] = _auth.Settings.AccountPassword,
                    ["OrdMktCode"] = "00",
                    ["BnsTpCode"] = "0",
                    ["IsuNo"] = "",
                    ["ExecYn"] = "0",
                    ["OrdDt"] = date.ToString("yyyyMMdd"),
                    ["SrtOrdNo2"] = 0,
                    ["BkseqTpCode"] = "0",
                    ["OrdPtnCode"] = "00"
                }
            };

            var request = await _auth.CreateRequestAsync(_http, HttpMethod.Post, "/stock/order",
                "CSPAQ13700", body, ct: ct);
            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[LS] 체결내역 조회 실패: {Status} {Body}", response.StatusCode, json);
                return [];
            }

            using var doc = JsonDocument.Parse(json);
            var orders = new List<BrokerOrder>();

            if (doc.RootElement.TryGetProperty("CSPAQ13700OutBlock3", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var ordNo = item.TryGetProperty("OrdNo", out var on) ? on.GetInt64().ToString() : "";
                    var symbol = item.TryGetProperty("IsuNo", out var isn) ? isn.GetString() ?? "" : "";
                    var bnsCode = item.TryGetProperty("BnsTpCode", out var bn) ? bn.GetString() : "0";
                    var ordQty = item.TryGetProperty("OrdQty", out var oq) ? oq.GetInt64() : 0;
                    var execQty = item.TryGetProperty("ExecQty", out var eq) ? eq.GetInt64() : 0;
                    var ordPrc = item.TryGetProperty("OrdPrc", out var op) ? op.GetDecimal() : 0;
                    var execPrc = item.TryGetProperty("ExecPrc", out var ep) ? ep.GetDecimal() : 0;

                    // A 접두어 제거
                    if (symbol.StartsWith("A"))
                        symbol = symbol[1..];

                    orders.Add(new BrokerOrder
                    {
                        OrderId = ordNo,
                        Symbol = symbol,
                        Direction = bnsCode == "2" ? TradeDirection.Long : TradeDirection.Short,
                        Quantity = (int)ordQty,
                        FilledQuantity = (int)execQty,
                        OrderPrice = ordPrc > 0 ? ordPrc : null,
                        AverageFillPrice = execPrc > 0 ? execPrc : null,
                        Status = execQty >= ordQty ? BrokerOrderStatus.Filled
                            : execQty > 0 ? BrokerOrderStatus.PartiallyFilled
                            : BrokerOrderStatus.Pending,
                        OrderType = BrokerOrderType.Limit,
                        SubmittedAt = DateTime.UtcNow
                    });
                }
            }

            _logger.LogInformation("[LS] 주문 내역 {Count}건 조회", orders.Count);
            return orders;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LS] 체결내역 조회 중 예외");
            return [];
        }
    }

    #endregion
}

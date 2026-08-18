using System.Globalization;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.Broker;

internal enum LsBrokerSide
{
    Sell,
    Buy
}

/// <summary>LS OPEN API의 현재 TR 코드, 블록 이름과 요청 모양을 소유합니다.</summary>
internal static class LsBrokerProtocol
{
    public const string OrderPath = "/stock/order";
    public const string AccountPath = "/stock/accno";
    public const string OrderTransactionCode = "CSPAT00601";
    public const string CancelTransactionCode = "CSPAT00801";
    public const string PositionsTransactionCode = "t0424";
    public const string AccountTransactionCode = "CSPAQ12300";
    public const string OrderHistoryTransactionCode = "CSPAQ13700";

    private const string OrderInputBlock = "CSPAT00601InBlock1";
    private const string CancelInputBlock = "CSPAT00801InBlock1";

    public static object CreateEntryOrderBody(
        LsSecuritiesSettings settings,
        TradeRecommendation recommendation) =>
        CreateOrderBody(
            settings,
            recommendation.Symbol,
            recommendation.ShareQuantity,
            recommendation.EntryPrice,
            LsBrokerSide.Buy,
            orderPriceType: "00");

    public static object CreateMarketOrderBody(
        LsSecuritiesSettings settings,
        string symbol,
        int quantity,
        LsBrokerSide side) =>
        CreateOrderBody(settings, symbol, quantity, 0m, side, orderPriceType: "03");

    public static object CreateCancelOrderBody(
        LsSecuritiesSettings settings,
        long orderNumber) =>
        new Dictionary<string, object>
        {
            [CancelInputBlock] = new Dictionary<string, object>
            {
                ["AcntNo"] = settings.AccountNo,
                ["InptPwd"] = settings.AccountPassword,
                ["OrgOrdNo"] = orderNumber,
                ["IsuNo"] = string.Empty,
                ["OrdQty"] = 0
            }
        };

    public static object CreatePositionsBody() => new Dictionary<string, object>
    {
        ["t0424InBlock"] = new Dictionary<string, object>
        {
            ["prcgb"] = "1",
            ["chegb"] = "2",
            ["dangb"] = "0",
            ["charge"] = "0",
            ["cts_expcode"] = string.Empty
        }
    };

    public static object CreateAccountBody() => new Dictionary<string, object>
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

    public static object CreateOrderHistoryBody(
        LsSecuritiesSettings settings,
        DateOnly koreanTradingDate) =>
        new Dictionary<string, object>
        {
            ["CSPAQ13700InBlock1"] = new Dictionary<string, object>
            {
                ["RecCnt"] = 300,
                ["AcntNo"] = settings.AccountNo,
                ["InptPwd"] = settings.AccountPassword,
                ["OrdMktCode"] = "00",
                ["BnsTpCode"] = "0",
                ["IsuNo"] = string.Empty,
                ["ExecYn"] = "0",
                ["OrdDt"] = koreanTradingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                ["SrtOrdNo2"] = 0,
                ["BkseqTpCode"] = "0",
                ["OrdPtnCode"] = "00"
            }
        };

    public static string NormalizeSymbol(string? symbol)
    {
        var value = symbol?.Trim() ?? string.Empty;
        return value.Length > 1 && value.StartsWith('A') ? value[1..] : value;
    }

    private static object CreateOrderBody(
        LsSecuritiesSettings settings,
        string symbol,
        int quantity,
        decimal price,
        LsBrokerSide side,
        string orderPriceType) =>
        new Dictionary<string, object>
        {
            [OrderInputBlock] = new Dictionary<string, object>
            {
                ["AcntNo"] = settings.AccountNo,
                ["InptPwd"] = settings.AccountPassword,
                ["IsuNo"] = $"A{NormalizeSymbol(symbol)}",
                ["OrdQty"] = quantity,
                ["OrdPrc"] = price,
                ["BnsTpCode"] = side == LsBrokerSide.Buy ? "2" : "1",
                ["OrdprcPtnCode"] = orderPriceType,
                ["MgntrnCode"] = "000",
                ["LoanDt"] = string.Empty,
                ["OrdCndiTpCode"] = "0"
            }
        };
}

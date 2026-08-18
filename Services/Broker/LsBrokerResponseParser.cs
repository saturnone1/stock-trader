using System.Globalization;
using System.Text.Json;
using StockTrader.Application.Accounts;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.Broker;

internal sealed record LsOrderHistoryParseResult(
    List<BrokerOrder> Orders,
    int InvalidTimestampCount,
    int InvalidQuantityCount);

/// <summary>LS JSON 응답을 브로커 독립 스냅샷으로 변환합니다.</summary>
internal static class LsBrokerResponseParser
{
    private const string CurrentOrderOutputBlock = "CSPAT00601OutBlock2";
    private const string LegacyOrderOutputBlock = "CSPAT00600OutBlock2";

    public static bool TryReadOrderId(string json, out string orderId)
    {
        orderId = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var blockName in new[]
                     {
                         CurrentOrderOutputBlock,
                         LegacyOrderOutputBlock
                     })
            {
                if (document.RootElement.TryGetProperty(blockName, out var block)
                    && TryReadText(block, "OrdNo", out orderId))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    public static IReadOnlyList<BrokerPositionSnapshot> ParsePositions(string json)
    {
        using var document = JsonDocument.Parse(json);
        var positions = new List<BrokerPositionSnapshot>();
        if (!document.RootElement.TryGetProperty("t0424OutBlock1", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return positions;
        }

        foreach (var item in items.EnumerateArray())
        {
            var quantity = ReadInt64(item, "janqty");
            if (quantity is <= 0 or > int.MaxValue) continue;
            TryReadText(item, "expcode", out var symbol);
            positions.Add(new BrokerPositionSnapshot(
                LsBrokerProtocol.NormalizeSymbol(symbol),
                (int)quantity,
                ReadDecimal(item, "pamt"),
                ReadDecimal(item, "price")));
        }

        return positions;
    }

    public static bool TryParseAccount(
        string json,
        string accountNumber,
        DateTime fetchedAtUtc,
        out BrokerAccount? account)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("CSPAQ12300OutBlock2", out var block))
        {
            account = null;
            return false;
        }

        account = new BrokerAccount
        {
            AccountId = accountNumber,
            TotalEquity = ReadDecimal(block, "DpsastTotamt"),
            Cash = ReadDecimal(block, "D2Dps"),
            BuyingPower = ReadDecimal(block, "MnyOrdAbleAmt"),
            UnrealizedPnL = ReadDecimal(block, "InvstOrgAmt"),
            FetchedAt = fetchedAtUtc,
            StatusMessage = "정상"
        };
        return true;
    }

    public static LsOrderHistoryParseResult ParseOrderHistory(
        string json,
        DateOnly requestedKoreanDate,
        TimeZoneInfo koreanTimeZone,
        DateTime fromUtc,
        DateTime toUtc)
    {
        fromUtc = LsOrderHistoryWindow.NormalizeUtc(fromUtc);
        toUtc = LsOrderHistoryWindow.NormalizeUtc(toUtc);
        using var document = JsonDocument.Parse(json);
        var orders = new List<BrokerOrder>();
        var invalidTimestamps = 0;
        var invalidQuantities = 0;
        if (!document.RootElement.TryGetProperty("CSPAQ13700OutBlock3", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return new LsOrderHistoryParseResult(orders, 0, 0);
        }

        var requestedDate = requestedKoreanDate.ToDateTime(TimeOnly.MinValue);
        foreach (var item in items.EnumerateArray())
        {
            if (!LsOrderTimestampParser.TryParseUtc(
                    item, requestedDate, koreanTimeZone, out var submittedAt))
            {
                invalidTimestamps++;
                continue;
            }
            if (submittedAt < fromUtc || submittedAt > toUtc) continue;

            var orderQuantity = ReadInt64(item, "OrdQty");
            var filledQuantity = ReadInt64(item, "ExecQty");
            if (orderQuantity is <= 0 or > int.MaxValue
                || filledQuantity is < 0 or > int.MaxValue)
            {
                invalidQuantities++;
                continue;
            }

            TryReadText(item, "OrdNo", out var orderId);
            TryReadText(item, "IsuNo", out var symbol);
            TryReadText(item, "BnsTpCode", out var sideCode);
            var orderPrice = ReadDecimal(item, "OrdPrc");
            var fillPrice = ReadDecimal(item, "ExecPrc");
            orders.Add(new BrokerOrder
            {
                OrderId = orderId,
                Symbol = LsBrokerProtocol.NormalizeSymbol(symbol),
                Direction = sideCode == "2" ? TradeDirection.Long : TradeDirection.Short,
                Quantity = (int)orderQuantity,
                FilledQuantity = (int)filledQuantity,
                OrderPrice = orderPrice > 0 ? orderPrice : null,
                AverageFillPrice = fillPrice > 0 ? fillPrice : null,
                Status = filledQuantity >= orderQuantity
                    ? BrokerOrderStatus.Filled
                    : filledQuantity > 0
                        ? BrokerOrderStatus.PartiallyFilled
                        : BrokerOrderStatus.Pending,
                OrderType = BrokerOrderType.Limit,
                SubmittedAt = submittedAt
            });
        }

        return new LsOrderHistoryParseResult(
            orders, invalidTimestamps, invalidQuantities);
    }

    private static decimal ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)) return 0;
        if (property.ValueKind == JsonValueKind.Number
            && property.TryGetDecimal(out var number))
        {
            return number;
        }
        return property.ValueKind == JsonValueKind.String
            && decimal.TryParse(
                property.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var textNumber)
            ? textNumber
            : 0;
    }

    private static long ReadInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)) return 0;
        if (property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out var number))
        {
            return number;
        }
        return property.ValueKind == JsonValueKind.String
            && long.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var textNumber)
            ? textNumber
            : 0;
    }

    private static bool TryReadText(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property)) return false;
        value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            _ => string.Empty
        };
        return !string.IsNullOrWhiteSpace(value);
    }
}

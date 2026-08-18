using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.Broker;

/// <summary>UTC 조회 구간을 한국 거래일 요청과 정확한 주문시각 필터로 변환합니다.</summary>
internal sealed class LsBrokerOrderHistoryClient(
    LsBrokerTransport transport,
    LsSecuritiesSettings settings,
    TimeZoneInfo koreanTimeZone,
    ILogger logger)
{
    public async Task<List<BrokerOrder>> GetAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        var fromUtc = LsOrderHistoryWindow.NormalizeUtc(from);
        var toUtc = LsOrderHistoryWindow.NormalizeUtc(to);
        var dates = LsOrderHistoryWindow.KoreanTradingDates(
            fromUtc, toUtc, koreanTimeZone);
        if (dates.Count == 0) return [];

        try
        {
            var allOrders = new List<BrokerOrder>();
            foreach (var date in dates)
            {
                var response = await transport.PostAsync(
                    LsBrokerProtocol.AccountPath,
                    LsBrokerProtocol.OrderHistoryTransactionCode,
                    LsBrokerProtocol.CreateOrderHistoryBody(settings, date),
                    ct);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogError(
                        "[LS] 체결내역 조회 실패: {Date} {Status} {Body}",
                        date,
                        response.StatusCode,
                        response.Body);
                    continue;
                }

                var parsed = LsBrokerResponseParser.ParseOrderHistory(
                    response.Body,
                    date,
                    koreanTimeZone,
                    fromUtc,
                    toUtc);
                if (parsed.InvalidTimestampCount > 0)
                {
                    logger.LogWarning(
                        "[LS] 실제 주문시각이 없는 주문내역 {Count}건 제외: {Date}",
                        parsed.InvalidTimestampCount,
                        date);
                }
                if (parsed.InvalidQuantityCount > 0)
                {
                    logger.LogWarning(
                        "[LS] 유효하지 않은 주문수량의 주문내역 {Count}건 제외: {Date}",
                        parsed.InvalidQuantityCount,
                        date);
                }
                allOrders.AddRange(parsed.Orders);
            }

            logger.LogInformation(
                "[LS] 주문 내역 {Count}건 조회 ({From:u}~{To:u})",
                allOrders.Count,
                fromUtc,
                toUtc);
            return allOrders;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[LS] 체결내역 조회 중 예외");
            return [];
        }
    }
}

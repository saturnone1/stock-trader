using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.Broker;

/// <summary>LS 주문 제출과 취소 프로토콜을 소유합니다.</summary>
internal sealed class LsBrokerOrderClient(
    LsBrokerTransport transport,
    LsSecuritiesSettings settings,
    TimeProvider timeProvider,
    ILogger logger)
{
    public async Task<BrokerOrder?> SubmitEntryAsync(
        TradeRecommendation recommendation,
        CancellationToken ct)
    {
        if (recommendation.ShareQuantity <= 0
            || string.IsNullOrWhiteSpace(recommendation.Symbol))
        {
            return null;
        }

        try
        {
            var response = await transport.PostAsync(
                LsBrokerProtocol.OrderPath,
                LsBrokerProtocol.OrderTransactionCode,
                LsBrokerProtocol.CreateEntryOrderBody(settings, recommendation),
                ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "[LS] 매수 주문 실패: {Status} {Body}",
                    response.StatusCode,
                    response.Body);
                return null;
            }

            if (!LsBrokerResponseParser.TryReadOrderId(response.Body, out var orderId))
            {
                logger.LogWarning(
                    "[LS] 진입 주문번호를 읽지 못함: {Symbol}",
                    recommendation.Symbol);
            }

            logger.LogInformation(
                "[LS] 매수 주문 성공: {Symbol} {Qty}주 @ {Price}",
                recommendation.Symbol,
                recommendation.ShareQuantity,
                recommendation.EntryPrice);
            return new BrokerOrder
            {
                OrderId = orderId,
                Symbol = LsBrokerProtocol.NormalizeSymbol(recommendation.Symbol),
                Direction = TradeDirection.Long,
                Quantity = recommendation.ShareQuantity,
                OrderPrice = recommendation.EntryPrice,
                Status = BrokerOrderStatus.Accepted,
                OrderType = BrokerOrderType.Limit,
                SubmittedAt = timeProvider.GetUtcNow().UtcDateTime
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "[LS] 매수 주문 중 예외: {Symbol}",
                recommendation.Symbol);
            return null;
        }
    }

    public async Task<BrokerOrder?> SubmitMarketAsync(
        string symbol,
        int quantity,
        LsBrokerSide side,
        CancellationToken ct)
    {
        var normalized = LsBrokerProtocol.NormalizeSymbol(symbol);
        if (quantity <= 0 || string.IsNullOrWhiteSpace(normalized)) return null;

        try
        {
            var response = await transport.PostAsync(
                LsBrokerProtocol.OrderPath,
                LsBrokerProtocol.OrderTransactionCode,
                LsBrokerProtocol.CreateMarketOrderBody(
                    settings, normalized, quantity, side),
                ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "[LS] 포지션 조정 실패: {Symbol} {Status} {Body}",
                    normalized,
                    response.StatusCode,
                    response.Body);
                return null;
            }

            if (!LsBrokerResponseParser.TryReadOrderId(response.Body, out var orderId))
            {
                logger.LogWarning(
                    "[LS] 포지션 조정 주문번호를 읽지 못함: {Symbol}",
                    normalized);
            }

            var direction = side == LsBrokerSide.Buy
                ? TradeDirection.Long
                : TradeDirection.Short;
            logger.LogInformation(
                "[LS] 포지션 조정 접수: {Direction} {Symbol} {Qty}주",
                direction,
                normalized,
                quantity);
            return new BrokerOrder
            {
                OrderId = orderId,
                Symbol = normalized,
                Direction = direction,
                Quantity = quantity,
                Status = BrokerOrderStatus.Accepted,
                OrderType = BrokerOrderType.Market,
                SubmittedAt = timeProvider.GetUtcNow().UtcDateTime
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "[LS] 포지션 조정 중 예외: {Symbol}",
                normalized);
            return null;
        }
    }

    public async Task<bool> CancelAsync(string orderId, CancellationToken ct)
    {
        if (!long.TryParse(orderId, out var orderNumber))
        {
            logger.LogWarning("[LS] 잘못된 주문번호 형식: {OrderId}", orderId);
            return false;
        }

        try
        {
            var response = await transport.PostAsync(
                LsBrokerProtocol.OrderPath,
                LsBrokerProtocol.CancelTransactionCode,
                LsBrokerProtocol.CreateCancelOrderBody(settings, orderNumber),
                ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "[LS] 주문 취소 실패: {OrderId} {Status} {Body}",
                    orderId,
                    response.StatusCode,
                    response.Body);
                return false;
            }

            logger.LogInformation("[LS] 주문 취소 성공: {OrderId}", orderId);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[LS] 주문 취소 중 예외: {OrderId}", orderId);
            return false;
        }
    }
}

using StockTrader.Application.Accounts;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.Broker;

/// <summary>LS 잔고와 계좌 스냅샷 조회 프로토콜을 소유합니다.</summary>
internal sealed class LsBrokerAccountClient(
    LsBrokerTransport transport,
    LsSecuritiesSettings settings,
    TimeProvider timeProvider,
    ILogger logger)
{
    public async Task<IReadOnlyList<BrokerPositionSnapshot>> GetPositionsAsync(
        CancellationToken ct)
    {
        try
        {
            var response = await transport.PostAsync(
                LsBrokerProtocol.AccountPath,
                LsBrokerProtocol.PositionsTransactionCode,
                LsBrokerProtocol.CreatePositionsBody(),
                ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "[LS] 잔고 조회 실패: {Status} {Body}",
                    response.StatusCode,
                    response.Body);
                return [];
            }

            var positions = LsBrokerResponseParser.ParsePositions(response.Body);
            logger.LogInformation("[LS] 보유 종목 {Count}건 조회", positions.Count);
            return positions;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[LS] 보유 종목 조회 중 예외");
            return [];
        }
    }

    public async Task<BrokerAccount?> GetAccountAsync(CancellationToken ct)
    {
        try
        {
            var response = await transport.PostAsync(
                LsBrokerProtocol.AccountPath,
                LsBrokerProtocol.AccountTransactionCode,
                LsBrokerProtocol.CreateAccountBody(),
                ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "[LS] 계좌 조회 실패: {Status} {Body}",
                    response.StatusCode,
                    response.Body);
                return null;
            }

            if (LsBrokerResponseParser.TryParseAccount(
                    response.Body,
                    settings.AccountNo,
                    timeProvider.GetUtcNow().UtcDateTime,
                    out var account))
            {
                return account;
            }

            logger.LogWarning("[LS] 계좌 응답에 OutBlock2 없음");
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[LS] 계좌 조회 중 예외");
            return null;
        }
    }
}

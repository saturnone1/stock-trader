using StockTrader.Application.Accounts;
using StockTrader.Models;
using StockTrader.Services.LsSecurities;

namespace StockTrader.Services.Broker;

/// <summary>
/// LS증권 브로커 기능을 목적별 프로토콜 클라이언트에 연결하는 얇은 facade입니다.
/// </summary>
public sealed class LsSecuritiesBrokerService : IBrokerService
{
    private readonly LsBrokerOrderClient _orders;
    private readonly LsBrokerAccountClient _account;
    private readonly LsBrokerOrderHistoryClient _history;
    private readonly ILogger _logger;

    public LsSecuritiesBrokerService(
        HttpClient http,
        LsAuthService auth,
        TimeProvider timeProvider,
        ILogger<LsSecuritiesBrokerService> logger)
    {
        http.BaseAddress = new Uri(auth.Settings.EffectiveBaseUrl);
        var transport = new LsBrokerTransport(http, auth);
        _orders = new LsBrokerOrderClient(transport, auth.Settings, timeProvider, logger);
        _account = new LsBrokerAccountClient(transport, auth.Settings, timeProvider, logger);
        _history = new LsBrokerOrderHistoryClient(
            transport, auth.Settings, LsAuthService.KstZone, logger);
        _logger = logger;
    }

    public BrokerType BrokerType => BrokerType.LsSecurities;

    public Task<BrokerOrder?> SubmitEntryOrderAsync(
        TradeRecommendation recommendation,
        CancellationToken ct = default) =>
        _orders.SubmitEntryAsync(recommendation, ct);

    public Task<BrokerOrder?> IncreasePositionAsync(
        string symbol,
        int quantity,
        CancellationToken ct = default) =>
        _orders.SubmitMarketAsync(symbol, quantity, LsBrokerSide.Buy, ct);

    public Task<bool> CancelOrderAsync(
        string orderId,
        CancellationToken ct = default) =>
        _orders.CancelAsync(orderId, ct);

    public Task<BrokerOrder?> ClosePositionAsync(
        string symbol,
        CancellationToken ct = default) =>
        ClosePositionCoreAsync(symbol, null, ct);

    public Task<BrokerOrder?> ClosePositionAsync(
        string symbol,
        int quantity,
        CancellationToken ct = default) =>
        ClosePositionCoreAsync(symbol, quantity, ct);

    public Task<IReadOnlyList<BrokerPositionSnapshot>> GetPositionsAsync(
        CancellationToken ct = default) =>
        _account.GetPositionsAsync(ct);

    public Task<BrokerAccount?> GetAccountAsync(CancellationToken ct = default) =>
        _account.GetAccountAsync(ct);

    public Task<List<BrokerOrder>> GetOrderHistoryAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct = default) =>
        _history.GetAsync(from, to, ct);

    private async Task<BrokerOrder?> ClosePositionCoreAsync(
        string symbol,
        int? requestedQuantity,
        CancellationToken ct)
    {
        try
        {
            var normalized = LsBrokerProtocol.NormalizeSymbol(symbol);
            var positions = await _account.GetPositionsAsync(ct);
            var position = positions.FirstOrDefault(item =>
                string.Equals(
                    LsBrokerProtocol.NormalizeSymbol(item.Symbol),
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
            if (position is null || position.Quantity <= 0)
            {
                _logger.LogWarning("[LS] 청산할 포지션 없음: {Symbol}", symbol);
                return null;
            }

            var sellQuantity = requestedQuantity ?? position.Quantity;
            if (sellQuantity <= 0 || sellQuantity > position.Quantity)
            {
                _logger.LogWarning(
                    "[LS] 잘못된 청산 수량: {Symbol} 요청={Requested} 보유={Available}",
                    symbol,
                    sellQuantity,
                    position.Quantity);
                return null;
            }

            return await _orders.SubmitMarketAsync(
                normalized, sellQuantity, LsBrokerSide.Sell, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[LS] 포지션 청산 중 예외: {Symbol}", symbol);
            return null;
        }
    }
}

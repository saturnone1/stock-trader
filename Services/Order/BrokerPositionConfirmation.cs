using StockTrader.Models;
using StockTrader.Services.Broker;

namespace StockTrader.Services.Order;

/// <summary>주문 제출 뒤 브로커가 노출하는 실제 포지션 체결 상태를 확인합니다.</summary>
internal static class BrokerPositionConfirmation
{
    public static async Task<Position?> WaitForAsync(
        IBrokerService broker,
        string symbol,
        CancellationToken ct)
    {
        const int maxAttempts = 10;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(500, ct);

            var positions = await broker.GetPositionsAsync(ct);
            if (positions is null)
                return null;
            var position = positions.FirstOrDefault(item => string.Equals(
                item.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            if (position is { EntryPrice: > 0, Quantity: > 0 })
                return position;
        }

        return null;
    }
}

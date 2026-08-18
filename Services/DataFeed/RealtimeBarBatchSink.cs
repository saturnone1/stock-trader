using System.Threading.Channels;
using StockTrader.Application.MarketData;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Services.DataFeed;

/// <summary>실시간 분봉을 한 트랜잭션으로 저장한 뒤 스캐너 채널에 종목을 발행합니다.</summary>
public sealed class RealtimeBarBatchSink(
    IServiceScopeFactory scopeFactory,
    Channel<string> symbolChannel) : IRealtimeBarBatchSink
{
    public async Task PersistAndPublishAsync(
        IReadOnlyList<OhlcvBar> bars,
        CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOhlcvRepository>();
        await repository.AddBarsAsync(bars, ct);

        foreach (var symbol in bars
                     .Select(bar => bar.Symbol)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await symbolChannel.Writer.WriteAsync(symbol, ct);
        }
    }
}

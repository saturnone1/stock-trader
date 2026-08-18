using StockTrader.Application.MarketData;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;

namespace StockTrader.Services.DataFeed;

/// <summary>사용자 설정을 실시간 공급자 선택 계약으로 투영하는 어댑터입니다.</summary>
public sealed class RealtimeMarketDataSelectionReader(
    ISettingsRepository settings) : IRealtimeMarketDataSelectionReader
{
    public async Task<RealtimeMarketDataSelection> ReadAsync(
        CancellationToken ct = default)
    {
        var value = await settings.GetAsync(ct);
        return new RealtimeMarketDataSelection(
            value.PreferredDataSource,
            MarketSymbolPolicy.NormalizeMany(value.WatchlistSymbols));
    }
}

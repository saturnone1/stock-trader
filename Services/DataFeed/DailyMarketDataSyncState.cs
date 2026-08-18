using System.Collections.Concurrent;
using StockTrader.Domain.MarketData;

namespace StockTrader.Services.DataFeed;

/// <summary>프로세스 수명 동안 공급자별 마지막 성공 시장일을 보관합니다.</summary>
public sealed class DailyMarketDataSyncState
{
    private readonly ConcurrentDictionary<DataSource, DateOnly> _marketDates = new();

    public bool WasCompleted(DataSource source, DateOnly marketDate) =>
        _marketDates.TryGetValue(source, out var completedAt)
        && completedAt == marketDate;

    public void MarkCompleted(DataSource source, DateOnly marketDate) =>
        _marketDates[source] = marketDate;
}

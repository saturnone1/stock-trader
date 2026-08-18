using StockTrader.Domain.MarketData;
using StockTrader.Models;

namespace StockTrader.Application.MarketData;

public enum DailyMarketDataSyncStatus
{
    NotReady,
    AlreadyCompleted,
    Completed,
    PartiallyFailed
}

public sealed record DailyMarketDataSyncResult(
    DailyMarketDataSyncStatus Status,
    int TotalSymbols = 0,
    int SyncedSymbols = 0,
    int SyncedBars = 0,
    int FailedSymbols = 0);

/// <summary>한 공급자 선택과 그 선택에 묶인 일봉 읽기·수집·저장 작업입니다.</summary>
public interface IDailyMarketDataSyncSession
{
    DataSource Source { get; }
    IReadOnlyList<string> WatchlistSymbols { get; }

    Task<IReadOnlyList<OhlcvBar>> LoadStoredBarsAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    Task<DateTime?> GetLastStoredBarAsync(
        string symbol,
        CancellationToken ct = default);

    Task<IReadOnlyList<OhlcvBar>> FetchBarsAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    Task SaveBarsAsync(
        IReadOnlyList<OhlcvBar> bars,
        CancellationToken ct = default);

    Task RefreshStatisticsAsync(CancellationToken ct = default);
}

public interface IDailyMarketDataSyncData
{
    Task<IDailyMarketDataSyncSession> OpenSessionAsync(
        CancellationToken ct = default);
}

/// <summary>초기 이력 복구와 공급자 시장별 정규 일봉 동기화를 실행합니다.</summary>
public interface IDailyMarketDataSyncCycle
{
    Task RunInitialSyncIfNeededAsync(CancellationToken ct = default);
    Task<DailyMarketDataSyncResult> RunScheduledAsync(CancellationToken ct = default);
}

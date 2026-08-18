using StockTrader.Domain.MarketData;
using StockTrader.Models;

namespace StockTrader.Application.MarketData;

public enum IntradayMarketDataIngestionStatus
{
    RealtimeStreamActive,
    RealtimeProviderTransition,
    MarketClosed,
    NoSymbols,
    Completed,
    PartiallyFailed
}

public sealed record IntradayMarketDataIngestionResult(
    IntradayMarketDataIngestionStatus Status,
    DataSource Source,
    int TotalSymbols = 0,
    int IngestedSymbols = 0,
    int FailedSymbols = 0);

/// <summary>한 공급자 선택과 그 선택에 묶인 최신 분봉 읽기·저장·알림 작업입니다.</summary>
public interface IIntradayMarketDataIngestionSession
{
    DataSource Source { get; }
    IReadOnlyList<string> WatchlistSymbols { get; }

    Task<OhlcvBar?> FetchLatestBarAsync(
        string symbol,
        CancellationToken ct = default);

    Task SaveBarsAsync(
        IReadOnlyList<OhlcvBar> bars,
        CancellationToken ct = default);

    Task PublishIngestedSymbolsAsync(
        IReadOnlyList<string> symbols,
        CancellationToken ct = default);
}

public interface IIntradayMarketDataIngestionData
{
    Task<IIntradayMarketDataIngestionSession> OpenSessionAsync(
        CancellationToken ct = default);
}

/// <summary>공급자별 실시간 시세가 REST 폴링을 대체할 수 있는지 알려주는 포트입니다.</summary>
public interface IRealtimeMarketDataStatus
{
    DataSource? ActiveSource { get; }
    DataSource? ConnectedSource { get; }
}

public sealed record RealtimeMarketDataSelection(
    DataSource Source,
    IReadOnlyList<string> WatchlistSymbols);

/// <summary>실시간 연결이 따라야 할 현재 공급자와 정규화된 관심종목을 읽습니다.</summary>
public interface IRealtimeMarketDataSelectionReader
{
    Task<RealtimeMarketDataSelection> ReadAsync(
        CancellationToken ct = default);
}

/// <summary>실시간 분봉 배치를 영속화한 뒤 해당 종목의 스캔 작업을 발행합니다.</summary>
public interface IRealtimeBarBatchSink
{
    Task PersistAndPublishAsync(
        IReadOnlyList<OhlcvBar> bars,
        CancellationToken ct = default);
}

/// <summary>실시간 콜백과 직렬화된 영속 배치 사이의 수명·역압력 경계입니다.</summary>
public interface IRealtimeBarIngestionBuffer
{
    void StartAccepting();
    void RejectNewBars();
    Task StopAcceptingAsync();
    Task ProcessAsync(OhlcvBar bar);
    Task<bool> FlushAsync(CancellationToken ct = default);
    Task RunFlushLoopAsync(CancellationToken ct);
    void Complete();
}

/// <summary>선택된 공급자의 정규장 중 최신 1분봉을 수집하고 저장한 뒤 스캐너에 알립니다.</summary>
public interface IIntradayMarketDataIngestionCycle
{
    Task<IntradayMarketDataIngestionResult> RunAsync(
        CancellationToken ct = default);
}

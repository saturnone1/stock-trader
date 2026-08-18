using StockTrader.Models;

namespace StockTrader.Application.Execution;

/// <summary>브로커가 접수한 신규 진입의 추천 상태와 포지션을 원자적으로 반영합니다.</summary>
public interface ILiveEntryExecutionStore
{
    Task CommitAcceptedEntryAsync(
        TradeRecommendation recommendation,
        Position position,
        CancellationToken ct = default);
}

/// <summary>수동 주문에 사용할 저장된 시그널을 조회하는 목적별 포트입니다.</summary>
public interface IManualOrderSignalStore
{
    Task<PatternSignal?> LoadAsync(long signalId, CancellationToken ct = default);
}

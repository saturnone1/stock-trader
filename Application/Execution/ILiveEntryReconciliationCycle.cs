namespace StockTrader.Application.Execution;

/// <summary>
/// 저장된 미확정 진입 주문을 소유 계좌별 브로커 증거와 한 번 재조정합니다.
/// </summary>
public interface ILiveEntryReconciliationCycle
{
    Task RunAsync(CancellationToken ct = default);
}

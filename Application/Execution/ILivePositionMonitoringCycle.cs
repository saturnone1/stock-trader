namespace StockTrader.Application.Execution;

/// <summary>
/// 저장된 오픈 포지션을 소유 계좌별 브로커 상태와 대조하고 공통 실행 정책을 한 번 평가합니다.
/// 백그라운드 호스트는 실행 주기만 소유합니다.
/// </summary>
public interface ILivePositionMonitoringCycle
{
    Task RunAsync(CancellationToken ct = default);
}

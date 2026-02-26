using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Broker;

namespace StockTrader.Services.Account;

/// <summary>
/// 멀티 계좌 관리 서비스 인터페이스.
///
/// 책임:
/// - TradingAccount CRUD (DB 영속화)
/// - 활성 계좌 전환 (한 번에 1개만 active)
/// - 계좌별 IBrokerService 인스턴스 런타임 캐시
/// - 계좌 연결 상태 조회
/// </summary>
public interface IAccountManager
{
    // ── 계좌 목록 ─────────────────────────────────────────────────────────

    /// <summary>모든 계좌 목록을 반환한다.</summary>
    Task<List<TradingAccount>> GetAllAccountsAsync(CancellationToken ct = default);

    /// <summary>현재 활성 계좌를 반환한다. 계좌가 없으면 null.</summary>
    Task<TradingAccount?> GetActiveAccountAsync(CancellationToken ct = default);

    /// <summary>특정 ID의 계좌를 반환한다.</summary>
    Task<TradingAccount?> GetAccountByIdAsync(int accountId, CancellationToken ct = default);

    // ── 계좌 CRUD ──────────────────────────────────────────────────────────

    /// <summary>
    /// 새 계좌를 추가한다.
    /// 첫 번째 계좌는 자동으로 활성 계좌가 된다.
    /// </summary>
    Task<TradingAccount> AddAccountAsync(TradingAccount account, CancellationToken ct = default);

    /// <summary>기존 계좌 정보를 수정한다.</summary>
    Task<TradingAccount> UpdateAccountAsync(TradingAccount account, CancellationToken ct = default);

    /// <summary>계좌를 삭제한다. 활성 계좌 삭제 시 다른 계좌를 자동으로 활성화한다.</summary>
    Task DeleteAccountAsync(int accountId, CancellationToken ct = default);

    // ── 활성 계좌 전환 ─────────────────────────────────────────────────────

    /// <summary>
    /// 지정된 계좌를 활성 계좌로 전환한다.
    /// 기존 활성 계좌는 자동으로 비활성화된다.
    /// </summary>
    Task SetActiveAccountAsync(int accountId, CancellationToken ct = default);

    // ── 브로커 서비스 접근 ─────────────────────────────────────────────────

    /// <summary>
    /// 활성 계좌의 IBrokerService를 반환한다.
    /// 계좌가 없으면 null (graceful degradation).
    /// </summary>
    IBrokerService? GetActiveBrokerService();

    /// <summary>
    /// 특정 계좌의 IBrokerService를 반환한다.
    /// 계좌가 존재하지 않거나 브로커 초기화에 실패하면 null.
    /// </summary>
    IBrokerService? GetBrokerServiceForAccount(int accountId);

    // ── 연결 상태 조회 ─────────────────────────────────────────────────────

    /// <summary>
    /// 모든 계좌의 연결 상태를 병렬로 조회한다.
    /// 실패한 계좌는 IsConnected=false로 반환 (예외 전파 없음).
    /// </summary>
    Task<List<AccountConnectionStatus>> GetAllConnectionStatusAsync(CancellationToken ct = default);

    /// <summary>특정 계좌의 연결 상태를 조회한다.</summary>
    Task<AccountConnectionStatus> GetConnectionStatusAsync(int accountId, CancellationToken ct = default);

    /// <summary>계좌 변경 이벤트 (UI 실시간 업데이트용)</summary>
    event Action? OnAccountsChanged;
}

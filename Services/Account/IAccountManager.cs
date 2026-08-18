using StockTrader.Application.Accounts;
using StockTrader.Services.Broker;

namespace StockTrader.Services.Account;

/// <summary>거래 계좌 관리와 계좌별 브로커 런타임 캐시의 조정 경계입니다.</summary>
public interface IAccountManager
{
    Task<IReadOnlyList<ManagedTradingAccount>> GetAllAccountsAsync(
        CancellationToken ct = default);

    Task<ManagedTradingAccount?> GetActiveAccountAsync(
        CancellationToken ct = default);

    Task<ManagedTradingAccount?> GetAccountByIdAsync(
        int accountId,
        CancellationToken ct = default);

    Task<ManagedTradingAccount> AddAccountAsync(
        ManagedTradingAccount account,
        CancellationToken ct = default);

    Task<ManagedTradingAccount> UpdateAccountAsync(
        ManagedTradingAccount account,
        CancellationToken ct = default);

    Task DeleteAccountAsync(int accountId, CancellationToken ct = default);

    Task SetActiveAccountAsync(int accountId, CancellationToken ct = default);

    Task<IBrokerService?> GetActiveBrokerServiceAsync(
        CancellationToken ct = default);

    Task<IBrokerService?> GetBrokerServiceForAccountAsync(
        int accountId,
        CancellationToken ct = default);

    /// <summary>
    /// 주문 전체에서 동일한 계좌 ID와 브로커 인스턴스를 사용하도록 하나의 스냅샷으로 해석합니다.
    /// accountId가 null이면 호출 시점의 활성 계좌를 사용합니다.
    /// </summary>
    Task<AccountBrokerContext?> GetBrokerContextAsync(
        int? accountId = null,
        CancellationToken ct = default);

    /// <summary>비활성화된 계좌라도 이미 제출된 주문의 읽기 전용 재조정을 허용합니다.</summary>
    Task<AccountBrokerContext?> GetBrokerContextForReconciliationAsync(
        int accountId,
        CancellationToken ct = default);

    /// <summary>비활성화된 계좌라도 이미 열린 포지션의 위험 축소 청산을 허용합니다.</summary>
    Task<AccountBrokerContext?> GetBrokerContextForPositionExitAsync(
        int accountId,
        CancellationToken ct = default);

    Task<IReadOnlyList<AccountConnectionStatus>> GetAllConnectionStatusAsync(
        CancellationToken ct = default);

    Task<AccountConnectionStatus> GetConnectionStatusAsync(
        int accountId,
        CancellationToken ct = default);

    event Action? OnAccountsChanged;
}

public sealed record AccountBrokerContext(
    ManagedTradingAccount Account,
    IBrokerService Broker);

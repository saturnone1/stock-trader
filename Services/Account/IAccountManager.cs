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

    Task<IReadOnlyList<AccountConnectionStatus>> GetAllConnectionStatusAsync(
        CancellationToken ct = default);

    Task<AccountConnectionStatus> GetConnectionStatusAsync(
        int accountId,
        CancellationToken ct = default);

    event Action? OnAccountsChanged;
}

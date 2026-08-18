namespace StockTrader.Application.Accounts;

public interface ITradingAccountStore
{
    Task<IReadOnlyList<ManagedTradingAccount>> LoadAllAsync(
        CancellationToken ct = default);

    Task<ManagedTradingAccount?> LoadActiveAsync(CancellationToken ct = default);

    Task<ManagedTradingAccount?> LoadByIdAsync(
        int accountId,
        CancellationToken ct = default);

    Task<ManagedTradingAccount> AddAsync(
        ManagedTradingAccount account,
        DateTime modifiedAt,
        CancellationToken ct = default);

    Task<ManagedTradingAccount?> UpdateAsync(
        ManagedTradingAccount account,
        DateTime modifiedAt,
        CancellationToken ct = default);

    Task<TradingAccountDeletion> DeleteAsync(
        int accountId,
        DateTime modifiedAt,
        CancellationToken ct = default);

    Task<bool> SetActiveAsync(
        int accountId,
        DateTime modifiedAt,
        CancellationToken ct = default);

    Task TouchLastConnectedAsync(
        int accountId,
        DateTime connectedAt,
        CancellationToken ct = default);
}

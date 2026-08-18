using System.Collections.Concurrent;
using StockTrader.Application.Accounts;
using StockTrader.Services.Broker;

namespace StockTrader.Services.Account;

/// <summary>
/// 영속화 포트와 브로커 팩토리를 조율하고 계좌별 브로커 인스턴스를 캐시합니다.
/// </summary>
public sealed class AccountManager : IAccountManager
{
    private const int NoActiveAccount = -1;

    private readonly ITradingAccountStore _store;
    private readonly IAccountBrokerServiceFactory _brokerFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountManager> _logger;
    private readonly ConcurrentDictionary<int, IBrokerService> _brokerCache = new();
    private int _activeAccountId = NoActiveAccount;

    public AccountManager(
        ITradingAccountStore store,
        IAccountBrokerServiceFactory brokerFactory,
        TimeProvider timeProvider,
        ILogger<AccountManager> logger)
    {
        _store = store;
        _brokerFactory = brokerFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public event Action? OnAccountsChanged;

    public Task<IReadOnlyList<ManagedTradingAccount>> GetAllAccountsAsync(
        CancellationToken ct = default) =>
        _store.LoadAllAsync(ct);

    public async Task<ManagedTradingAccount?> GetActiveAccountAsync(
        CancellationToken ct = default)
    {
        var cachedId = Volatile.Read(ref _activeAccountId);
        if (cachedId != NoActiveAccount)
        {
            var cached = await _store.LoadByIdAsync(cachedId, ct);
            if (cached is { IsActive: true, IsEnabled: true })
                return cached;
            Interlocked.CompareExchange(
                ref _activeAccountId,
                NoActiveAccount,
                cachedId);
        }

        var active = await _store.LoadActiveAsync(ct);
        if (active is not null)
        {
            Interlocked.CompareExchange(
                ref _activeAccountId,
                active.Id,
                NoActiveAccount);
        }
        return active;
    }

    public Task<ManagedTradingAccount?> GetAccountByIdAsync(
        int accountId,
        CancellationToken ct = default) =>
        _store.LoadByIdAsync(accountId, ct);

    public async Task<ManagedTradingAccount> AddAccountAsync(
        ManagedTradingAccount account,
        CancellationToken ct = default)
    {
        EnsureValid(account);
        var created = await _store.AddAsync(account, UtcNow, ct);
        if (created.IsActive)
            Interlocked.Exchange(ref _activeAccountId, created.Id);

        _logger.LogInformation(
            "Account added: [{Id}] {Name} ({BrokerType})",
            created.Id,
            created.AccountName,
            created.BrokerType);
        OnAccountsChanged?.Invoke();
        return created;
    }

    public async Task<ManagedTradingAccount> UpdateAccountAsync(
        ManagedTradingAccount account,
        CancellationToken ct = default)
    {
        EnsureValid(account);
        var updated = await _store.UpdateAsync(account, UtcNow, ct)
            ?? throw new InvalidOperationException($"Account {account.Id} not found.");
        _brokerCache.TryRemove(account.Id, out _);
        if (updated.IsActive && updated.IsEnabled)
        {
            Interlocked.Exchange(ref _activeAccountId, updated.Id);
        }
        else
        {
            Interlocked.CompareExchange(
                ref _activeAccountId,
                NoActiveAccount,
                updated.Id);
        }

        _logger.LogInformation(
            "Account updated: [{Id}] {Name}",
            updated.Id,
            updated.AccountName);
        OnAccountsChanged?.Invoke();
        return updated;
    }

    public async Task DeleteAccountAsync(
        int accountId,
        CancellationToken ct = default)
    {
        var result = await _store.DeleteAsync(accountId, UtcNow, ct);
        if (!result.Deleted)
            return;

        _brokerCache.TryRemove(accountId, out _);
        if (result.DeletedWasActive)
        {
            Interlocked.Exchange(
                ref _activeAccountId,
                result.ActivatedAccountId ?? NoActiveAccount);
            if (result.ActivatedAccountId.HasValue)
            {
                _logger.LogInformation(
                    "Active account switched to [{Id}] after deletion",
                    result.ActivatedAccountId.Value);
            }
        }

        _logger.LogInformation("Account deleted: [{Id}]", accountId);
        OnAccountsChanged?.Invoke();
    }

    public async Task SetActiveAccountAsync(
        int accountId,
        CancellationToken ct = default)
    {
        var account = await _store.LoadByIdAsync(accountId, ct)
            ?? throw new InvalidOperationException($"Account {accountId} not found.");
        if (!account.IsEnabled)
            throw new InvalidOperationException("A disabled account cannot be activated.");
        if (!BrokerCatalog.Get(account.BrokerType).IsImplemented)
            throw new InvalidOperationException("This broker integration is not available yet.");
        if (!await _store.SetActiveAsync(accountId, UtcNow, ct))
            throw new InvalidOperationException($"Account {accountId} could not be activated.");

        Interlocked.Exchange(ref _activeAccountId, accountId);
        _logger.LogInformation("Active account set to [{Id}]", accountId);
        OnAccountsChanged?.Invoke();
    }

    public async Task<IBrokerService?> GetActiveBrokerServiceAsync(
        CancellationToken ct = default) =>
        (await GetBrokerContextAsync(ct: ct))?.Broker;

    public async Task<IBrokerService?> GetBrokerServiceForAccountAsync(
        int accountId,
        CancellationToken ct = default) =>
        (await GetBrokerContextAsync(accountId, ct))?.Broker;

    public async Task<AccountBrokerContext?> GetBrokerContextAsync(
        int? accountId = null,
        CancellationToken ct = default)
    {
        var account = accountId.HasValue
            ? await _store.LoadByIdAsync(accountId.Value, ct)
            : await GetActiveAccountAsync(ct);
        if (account is not { IsEnabled: true }
            || !BrokerCatalog.Get(account.BrokerType).IsImplemented)
            return null;

        if (_brokerCache.TryGetValue(account.Id, out var cached))
            return new AccountBrokerContext(account, cached);
        var broker = GetOrCreateBrokerService(account);
        return broker is null ? null : new AccountBrokerContext(account, broker);
    }

    public async Task<AccountBrokerContext?> GetBrokerContextForReconciliationAsync(
        int accountId,
        CancellationToken ct = default)
    {
        var account = await _store.LoadByIdAsync(accountId, ct);
        if (account is null)
            return null;
        if (_brokerCache.TryGetValue(account.Id, out var cached))
            return new AccountBrokerContext(account, cached);
        var broker = GetOrCreateBrokerService(account);
        return broker is null ? null : new AccountBrokerContext(account, broker);
    }

    public async Task<IReadOnlyList<AccountConnectionStatus>>
        GetAllConnectionStatusAsync(CancellationToken ct = default)
    {
        var accounts = await _store.LoadAllAsync(ct);
        return await Task.WhenAll(
            accounts.Select(account => GetConnectionStatusAsync(account.Id, ct)));
    }

    public async Task<AccountConnectionStatus> GetConnectionStatusAsync(
        int accountId,
        CancellationToken ct = default)
    {
        var checkedAt = UtcNow;
        try
        {
            var account = await _store.LoadByIdAsync(accountId, ct);
            if (account is null)
                return FailedConnectionStatus(accountId, checkedAt, "계좌를 찾을 수 없습니다.");
            var capabilities = BrokerCatalog.Get(account.BrokerType).Capabilities;
            if (!capabilities.CanReadAccount || !capabilities.CanReadPositions)
            {
                return FailedConnectionStatus(
                    accountId,
                    checkedAt,
                    "선택한 브로커는 계좌 및 보유 종목 조회를 아직 지원하지 않습니다.");
            }

            var broker = await GetBrokerServiceForAccountAsync(accountId, ct);
            if (broker is null)
            {
                return FailedConnectionStatus(
                    accountId,
                    checkedAt,
                    "브로커 서비스 초기화 실패");
            }

            var brokerAccount = await broker.GetAccountAsync(ct);
            if (brokerAccount is null)
            {
                return FailedConnectionStatus(
                    accountId,
                    checkedAt,
                    "계좌 조회 실패");
            }

            var positions = await broker.GetPositionsAsync(ct);
            await TouchLastConnectedAsync(accountId, checkedAt, ct);
            return new AccountConnectionStatus
            {
                AccountId = accountId,
                IsConnected = true,
                StatusMessage = brokerAccount.IsTradingBlocked ? "거래 정지" : "정상",
                TotalEquity = brokerAccount.TotalEquity,
                Cash = brokerAccount.Cash,
                BuyingPower = brokerAccount.BuyingPower,
                OpenPositionCount = positions.Count,
                CheckedAt = checkedAt
            };
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to get connection status for account [{Id}]",
                accountId);
            return FailedConnectionStatus(
                accountId,
                checkedAt,
                $"오류: {exception.Message}");
        }
    }

    private IBrokerService? GetOrCreateBrokerService(ManagedTradingAccount account)
    {
        try
        {
            var created = _brokerFactory.Create(account);
            return _brokerCache.GetOrAdd(account.Id, created);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to create broker service for account [{Id}] {Name}",
                account.Id,
                account.AccountName);
            return null;
        }
    }

    private async Task TouchLastConnectedAsync(
        int accountId,
        DateTime connectedAt,
        CancellationToken ct)
    {
        try
        {
            await _store.TouchLastConnectedAsync(accountId, connectedAt, ct);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Failed to update LastConnectedAt for account [{Id}]",
                accountId);
        }
    }

    private static AccountConnectionStatus FailedConnectionStatus(
        int accountId,
        DateTime checkedAt,
        string message) => new()
        {
            AccountId = accountId,
            StatusMessage = message,
            CheckedAt = checkedAt
        };

    private static void EnsureValid(ManagedTradingAccount account)
    {
        var validation = TradingAccountPolicy.Validate(account);
        if (!validation.Succeeded)
            throw new ArgumentException(string.Join(" ", validation.Errors));
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;
}

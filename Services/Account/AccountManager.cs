using Alpaca.Markets;
using Microsoft.EntityFrameworkCore;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Broker;

namespace StockTrader.Services.Account;

/// <summary>
/// 멀티 계좌 관리 서비스 구현체.
///
/// 설계 결정:
/// - IBrokerService 인스턴스는 계좌 ID를 키로 인메모리 딕셔너리에 캐시 (재생성 비용 방지)
/// - Singleton 등록: 계좌 상태는 앱 전체에서 공유되어야 함
/// - AppDbContext는 IDbContextFactory로 주입 (singleton에서 scoped context 안전 사용)
/// - AlpacaBrokerService는 IOptions 대신 직접 SecretKey로 초기화 (계좌별 키 지원)
/// </summary>
public class AccountManager : IAccountManager
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<AccountManager> _logger;

    // 계좌 ID → IBrokerService 런타임 캐시 (ConcurrentDictionary: 스레드 안전)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, IBrokerService> _brokerCache = new();

    // 현재 활성 계좌 ID 캐시 (DB 왕복 최소화, Interlocked/Volatile로 스레드 안전 접근)
    private const int NoActiveAccount = -1;
    private int _activeAccountId = NoActiveAccount;

    public event Action? OnAccountsChanged;

    public AccountManager(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<AccountManager> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    // ── 계좌 목록 ─────────────────────────────────────────────────────────

    public async Task<List<TradingAccount>> GetAllAccountsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TradingAccounts
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<TradingAccount?> GetActiveAccountAsync(CancellationToken ct = default)
    {
        // _activeAccountId가 유효한 값으로 캐시되어 있으면 DB 조회 없이 직접 반환한다.
        // SetActiveAccountAsync/AddAccountAsync/DeleteAccountAsync가 Interlocked로 이 값을 항상 동기화한다.
        var cachedId = Volatile.Read(ref _activeAccountId);
        if (cachedId != NoActiveAccount)
            return await GetAccountByIdAsync(cachedId, ct);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var account = await db.TradingAccounts
            .Where(a => a.IsActive && a.IsEnabled)
            .FirstOrDefaultAsync(ct);

        // DB 결과를 캐시에 동기화 (최초 로드 또는 앱 재시작 후 복원)
        if (account != null)
            Interlocked.CompareExchange(ref _activeAccountId, account.Id, NoActiveAccount);

        return account;
    }

    public async Task<TradingAccount?> GetAccountByIdAsync(int accountId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TradingAccounts.FindAsync([accountId], ct);
    }

    // ── 계좌 CRUD ──────────────────────────────────────────────────────────

    public async Task<TradingAccount> AddAccountAsync(TradingAccount account, CancellationToken ct = default)
    {
        account.CreatedAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // 첫 번째 계좌라면 자동으로 활성화
        var existingCount = await db.TradingAccounts.CountAsync(ct);
        if (existingCount == 0)
        {
            account.IsActive = true;
        }
        else if (account.IsActive)
        {
            // 명시적으로 활성 계좌로 추가 요청 시 기존 활성 계좌 비활성화
            await db.TradingAccounts
                .Where(a => a.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsActive, false), ct);
        }

        db.TradingAccounts.Add(account);
        await db.SaveChangesAsync(ct);

        if (account.IsActive)
            Interlocked.Exchange(ref _activeAccountId, account.Id);

        _logger.LogInformation("Account added: [{Id}] {Name} ({BrokerType})",
            account.Id, account.AccountName, account.BrokerType);

        OnAccountsChanged?.Invoke();
        return account;
    }

    public async Task<TradingAccount> UpdateAccountAsync(TradingAccount account, CancellationToken ct = default)
    {
        account.UpdatedAt = DateTime.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.TradingAccounts.Update(account);
        await db.SaveChangesAsync(ct);

        // 캐시된 브로커 인스턴스 무효화 (API 키가 변경될 수 있음)
        await InvalidateBrokerCacheAsync(account.Id);

        // 비활성화된 계좌가 현재 활성 캐시와 일치하면 캐시 무효화
        // 이렇게 하지 않으면 GetActiveAccountAsync가 DB를 건너뛰고 비활성 계좌를 반환한다
        if (!account.IsEnabled || !account.IsActive)
        {
            var cachedId = Volatile.Read(ref _activeAccountId);
            if (cachedId == account.Id)
            {
                Interlocked.CompareExchange(ref _activeAccountId, NoActiveAccount, cachedId);
                _logger.LogInformation(
                    "Active account cache invalidated: account [{Id}] is no longer active/enabled", account.Id);
            }
        }

        _logger.LogInformation("Account updated: [{Id}] {Name}", account.Id, account.AccountName);
        OnAccountsChanged?.Invoke();
        return account;
    }

    public async Task DeleteAccountAsync(int accountId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var account = await db.TradingAccounts.FindAsync([accountId], ct);
        if (account == null) return;

        var wasActive = account.IsActive;
        db.TradingAccounts.Remove(account);
        await db.SaveChangesAsync(ct);

        // 삭제한 계좌가 활성 계좌였으면 다른 계좌를 활성화
        // AsNoTracking: 삭제 후 change tracker에 잔여 엔티티가 없도록 별도 쿼리
        if (wasActive)
        {
            var nextId = await db.TradingAccounts
                .AsNoTracking()
                .Where(a => a.IsEnabled)
                .OrderBy(a => a.CreatedAt)
                .Select(a => (int?)a.Id)
                .FirstOrDefaultAsync(ct);

            if (nextId.HasValue)
            {
                await db.TradingAccounts
                    .Where(a => a.Id == nextId.Value)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.IsActive, true)
                        .SetProperty(a => a.UpdatedAt, DateTime.UtcNow), ct);

                Interlocked.Exchange(ref _activeAccountId, nextId.Value);

                _logger.LogInformation("Active account switched to [{Id}] after deletion", nextId.Value);
            }
            else
            {
                Interlocked.Exchange(ref _activeAccountId, NoActiveAccount);
            }
        }

        // 캐시 제거
        await InvalidateBrokerCacheAsync(accountId);

        _logger.LogInformation("Account deleted: [{Id}]", accountId);
        OnAccountsChanged?.Invoke();
    }

    // ── 활성 계좌 전환 ─────────────────────────────────────────────────────

    public async Task SetActiveAccountAsync(int accountId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // 모든 계좌 비활성화 후 지정 계좌만 활성화 (단일 활성 계좌 불변식 유지)
        await db.TradingAccounts
            .Where(a => a.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsActive, false), ct);

        var rows = await db.TradingAccounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsActive, true)
                .SetProperty(a => a.UpdatedAt, DateTime.UtcNow), ct);

        if (rows == 0)
            throw new InvalidOperationException($"Account {accountId} not found.");

        Interlocked.Exchange(ref _activeAccountId, accountId);
        _logger.LogInformation("Active account set to [{Id}]", accountId);
        OnAccountsChanged?.Invoke();
    }

    // ── 브로커 서비스 접근 ─────────────────────────────────────────────────

    /// <inheritdoc />
    public IBrokerService? GetActiveBrokerService()
    {
        var activeId = Volatile.Read(ref _activeAccountId);
        if (activeId == NoActiveAccount) return null;
        _brokerCache.TryGetValue(activeId, out var cached);
        return cached;
    }

    /// <inheritdoc />
    public IBrokerService? GetBrokerServiceForAccount(int accountId)
    {
        _brokerCache.TryGetValue(accountId, out var cached);
        return cached;
    }

    public async Task<IBrokerService?> GetActiveBrokerServiceAsync(CancellationToken ct = default)
    {
        // 캐시에서 활성 계좌의 브로커 서비스 반환
        var activeId = Volatile.Read(ref _activeAccountId);
        if (activeId != NoActiveAccount && _brokerCache.TryGetValue(activeId, out var cached))
            return cached;

        var account = await GetActiveAccountAsync(ct);
        if (account == null) return null;

        return await GetOrCreateBrokerServiceAsync(account);
    }

    public async Task<IBrokerService?> GetBrokerServiceForAccountAsync(int accountId, CancellationToken ct = default)
    {
        if (_brokerCache.TryGetValue(accountId, out var cached))
            return cached;

        var account = await GetAccountByIdAsync(accountId, ct);
        if (account == null) return null;

        return await GetOrCreateBrokerServiceAsync(account);
    }

    // ── 연결 상태 조회 ─────────────────────────────────────────────────────

    public async Task<List<AccountConnectionStatus>> GetAllConnectionStatusAsync(CancellationToken ct = default)
    {
        var accounts = await GetAllAccountsAsync(ct);

        // 병렬 조회 (계좌당 독립 브로커 호출)
        var tasks = accounts.Select(a => GetConnectionStatusAsync(a.Id, ct));
        var results = await Task.WhenAll(tasks);
        return [.. results];
    }

    public async Task<AccountConnectionStatus> GetConnectionStatusAsync(int accountId, CancellationToken ct = default)
    {
        var status = new AccountConnectionStatus { AccountId = accountId };

        try
        {
            var broker = await GetBrokerServiceForAccountAsync(accountId, ct);
            if (broker == null)
            {
                status.StatusMessage = "브로커 서비스 초기화 실패";
                return status;
            }

            var accountInfo = await broker.GetAccountAsync(ct);
            if (accountInfo == null)
            {
                status.StatusMessage = "계좌 조회 실패";
                return status;
            }

            var positions = await broker.GetPositionsAsync(ct);

            status.IsConnected = true;
            status.StatusMessage = accountInfo.IsTradingBlocked ? "거래 정지" : "정상";
            status.TotalEquity = accountInfo.TotalEquity;
            status.Cash = accountInfo.Cash;
            status.BuyingPower = accountInfo.BuyingPower;
            status.OpenPositionCount = positions.Count;
            status.CheckedAt = DateTime.UtcNow;

            // 마지막 연결 시각 업데이트 (fire-and-forget)
            _ = UpdateLastConnectedAsync(accountId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get connection status for account [{Id}]", accountId);
            status.StatusMessage = $"오류: {ex.Message}";
        }

        return status;
    }

    // ── Private helpers ────────────────────────────────────────────────────

    /// <summary>
    /// 계좌 정보를 바탕으로 IBrokerService를 생성하거나 캐시에서 반환한다.
    /// null 결과는 캐시하지 않아 다음 호출 시 재시도.
    /// </summary>
    private Task<IBrokerService?> GetOrCreateBrokerServiceAsync(TradingAccount account)
    {
        if (_brokerCache.TryGetValue(account.Id, out var cached))
            return Task.FromResult<IBrokerService?>(cached);

        var service = CreateBrokerService(account);
        if (service != null)
            _brokerCache.TryAdd(account.Id, service);

        return Task.FromResult<IBrokerService?>(service);
    }

    /// <summary>
    /// 계좌 정보로부터 브로커 서비스 인스턴스를 직접 생성한다.
    /// DI 컨테이너를 거치지 않고 계좌별 API 키로 초기화한다.
    /// </summary>
    private IBrokerService? CreateBrokerService(TradingAccount account)
    {
        try
        {
            return account.BrokerType switch
            {
                BrokerType.Alpaca => CreateAlpacaService(account),
                BrokerType.KoreaInvestment => CreateKoreaInvestmentService(account),
                BrokerType.Kiwoom => CreateKiwoomService(account),
                _ => throw new ArgumentOutOfRangeException(nameof(account.BrokerType),
                    $"Unsupported broker type: {account.BrokerType}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create broker service for account [{Id}] {Name}",
                account.Id, account.AccountName);
            return null;
        }
    }

    private IBrokerService CreateAlpacaService(TradingAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.ApiSecret))
        {
            // Error 레벨: 이후 모든 브로커 API 호출이 401로 실패하므로 즉시 인지해야 함
            _logger.LogError(
                "Account [{Id}] {Name}: API key or secret is not configured. " +
                "All broker API calls will fail with 401. " +
                "Set ApiKey and ApiSecret in the account settings.",
                account.Id, account.AccountName);

            // 의미있는 예외를 던져 GetOrCreateBrokerServiceAsync가 null을 반환하게 한다.
            // 호출부(GetConnectionStatusAsync 등)는 null 체크 후 사용자에게 명확한 메시지를 표시한다.
            throw new InvalidOperationException(
                $"계좌 [{account.AccountName}]의 API 키가 설정되지 않았습니다. " +
                "계좌 관리 화면에서 API Key와 Secret을 입력해 주세요.");
        }

        var isPaper = !string.Equals(account.Environment, "Live", StringComparison.OrdinalIgnoreCase);
        var logger = _logger as ILogger<AlpacaBrokerService>
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AlpacaBrokerService>.Instance;

        return new DynamicAlpacaBrokerService(account.ApiKey, account.ApiSecret, isPaper, logger);
    }

    private IBrokerService CreateKoreaInvestmentService(TradingAccount account)
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<KoreaInvestmentBrokerService>.Instance;
        return new KoreaInvestmentBrokerService(logger);
    }

    private IBrokerService CreateKiwoomService(TradingAccount account)
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<KiwoomBrokerService>.Instance;
        return new KiwoomBrokerService(logger);
    }

    private Task InvalidateBrokerCacheAsync(int accountId)
    {
        _brokerCache.TryRemove(accountId, out _);
        return Task.CompletedTask;
    }

    private async Task UpdateLastConnectedAsync(int accountId, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            await db.TradingAccounts
                .Where(a => a.Id == accountId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastConnectedAt, DateTime.UtcNow), ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to update LastConnectedAt for account [{Id}]", accountId);
        }
    }
}

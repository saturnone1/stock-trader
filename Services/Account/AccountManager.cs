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

    // 계좌 ID → IBrokerService 런타임 캐시
    private readonly Dictionary<int, IBrokerService> _brokerCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    // 현재 활성 계좌 ID 캐시 (DB 왕복 최소화)
    private int? _activeAccountId;

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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TradingAccounts
            .Where(a => a.IsActive && a.IsEnabled)
            .FirstOrDefaultAsync(ct);
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
            _activeAccountId = account.Id;

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
        if (wasActive)
        {
            var next = await db.TradingAccounts
                .Where(a => a.IsEnabled)
                .OrderBy(a => a.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (next != null)
            {
                next.IsActive = true;
                next.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                _activeAccountId = next.Id;

                _logger.LogInformation("Active account switched to [{Id}] {Name} after deletion",
                    next.Id, next.AccountName);
            }
            else
            {
                _activeAccountId = null;
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

        _activeAccountId = accountId;
        _logger.LogInformation("Active account set to [{Id}]", accountId);
        OnAccountsChanged?.Invoke();
    }

    // ── 브로커 서비스 접근 ─────────────────────────────────────────────────

    public IBrokerService? GetActiveBrokerService()
    {
        // 캐시에서 활성 계좌의 브로커 서비스 반환
        if (_activeAccountId.HasValue && _brokerCache.TryGetValue(_activeAccountId.Value, out var cached))
            return cached;

        // 캐시 미스: DB에서 활성 계좌 조회 (SynchronizationContext 데드락 방지)
        var account = Task.Run(() => GetActiveAccountAsync()).GetAwaiter().GetResult();
        if (account == null) return null;

        return GetOrCreateBrokerService(account);
    }

    public IBrokerService? GetBrokerServiceForAccount(int accountId)
    {
        if (_brokerCache.TryGetValue(accountId, out var cached))
            return cached;

        var account = Task.Run(() => GetAccountByIdAsync(accountId)).GetAwaiter().GetResult();
        if (account == null) return null;

        return GetOrCreateBrokerService(account);
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
            var broker = GetBrokerServiceForAccount(accountId);
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
    /// </summary>
    private IBrokerService? GetOrCreateBrokerService(TradingAccount account)
    {
        _cacheLock.Wait();
        try
        {
            if (_brokerCache.TryGetValue(account.Id, out var existing))
                return existing;

            var service = CreateBrokerService(account);
            if (service != null)
                _brokerCache[account.Id] = service;

            return service;
        }
        finally
        {
            _cacheLock.Release();
        }
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
            _logger.LogWarning("Account [{Id}] {Name}: API key or secret is empty",
                account.Id, account.AccountName);
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

    private async Task InvalidateBrokerCacheAsync(int accountId)
    {
        await _cacheLock.WaitAsync();
        try
        {
            _brokerCache.Remove(accountId);
        }
        finally
        {
            _cacheLock.Release();
        }
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

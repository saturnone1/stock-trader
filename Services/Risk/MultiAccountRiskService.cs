using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Services.Account;

namespace StockTrader.Services.Risk;

/// <summary>
/// 멀티 계좌 환경에서의 리스크 관리 서비스.
///
/// 핵심 설계 원칙:
/// - 각 계좌(AccountId)는 독립적인 RiskState를 유지 → 계좌 A의 손실이 계좌 B에 영향 없음
/// - 전체 포트폴리오(PortfolioRiskState)는 모든 계좌의 합산으로 별도 추적
/// - Singleton 등록: 리스크 상태는 앱 전체에서 공유 (Scoped → 매 요청마다 상태가 초기화되는 버그 방지)
/// - IServiceScopeFactory로 Scoped 리포지토리 안전 접근
/// </summary>
public class MultiAccountRiskService : IRiskManagementService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TradingSettings _tradingSettings;
    private readonly IAccountManager _accountManager;
    private readonly ILogger<MultiAccountRiskService> _logger;

    // 계좌 ID → 독립 RiskState (ConcurrentDictionary: 스레드 안전)
    private readonly ConcurrentDictionary<int, RiskState> _accountRiskStates = new();

    // 전체 포트폴리오 리스크 (모든 계좌 합산)
    private volatile RiskState _portfolioRiskState = new();

    // 활성 계좌가 없는 경우의 기본 상태
    private volatile RiskState _fallbackRiskState = new();

    public MultiAccountRiskService(
        IServiceScopeFactory scopeFactory,
        IOptions<TradingSettings> tradingSettings,
        IAccountManager accountManager,
        ILogger<MultiAccountRiskService> logger)
    {
        _scopeFactory = scopeFactory;
        _tradingSettings = tradingSettings.Value;
        _accountManager = accountManager;
        _logger = logger;
    }

    // ── IRiskManagementService 구현 (활성 계좌 기준) ──────────────────────

    /// <summary>
    /// 활성 계좌의 RiskState를 반환한다.
    /// 활성 계좌가 없으면 빈 상태(안전 기본값) 반환.
    /// </summary>
    public async Task<RiskState> GetCurrentRiskStateAsync(CancellationToken ct = default)
    {
        var activeAccount = await _accountManager.GetActiveAccountAsync(ct);
        if (activeAccount == null)
            return _fallbackRiskState;

        return _accountRiskStates.TryGetValue(activeAccount.Id, out var state)
            ? state
            : _fallbackRiskState;
    }

    /// <summary>포트폴리오 전체 리스크 상태를 반환한다.</summary>
    public RiskState GetPortfolioRiskState() => _portfolioRiskState;

    /// <summary>특정 계좌의 RiskState를 반환한다.</summary>
    public RiskState GetAccountRiskState(int accountId) =>
        _accountRiskStates.TryGetValue(accountId, out var state) ? state : new RiskState();

    public async Task<(bool Allowed, string Reason)> CanOpenPositionAsync(
        string symbol, string sector, CancellationToken ct = default)
    {
        var activeAccount = await _accountManager.GetActiveAccountAsync(ct);
        if (activeAccount == null)
            return (false, "활성 계좌가 없습니다. 계좌 관리에서 계좌를 추가하세요.");

        var riskState = GetAccountRiskState(activeAccount.Id);
        if (riskState.IsTradingHalted)
            return (false, "Trading halted: daily loss limit reached");

        using var scope = _scopeFactory.CreateScope();
        var tradeRepo = scope.ServiceProvider.GetRequiredService<ITradeRepository>();
        var allOpenPositions = await tradeRepo.GetOpenPositionsAsync(ct);

        // 이 계좌의 포지션만 필터링.
        // AccountId == 0은 레거시(계좌 미지정) 데이터이므로 활성 계좌 포지션으로 포함한다.
        var accountPositions = allOpenPositions
            .Where(p => p.AccountId == activeAccount.Id || p.AccountId == 0)
            .ToList();

        if (accountPositions.Count >= _tradingSettings.MaxTotalPositions)
            return (false, $"Max total positions ({_tradingSettings.MaxTotalPositions}) reached");

        if (accountPositions.Any(p => p.Symbol == symbol))
            return (false, $"Already have open position in {symbol}");

        if (!string.IsNullOrEmpty(sector))
        {
            var sectorCount = accountPositions.Count(p => p.Sector == sector);
            if (sectorCount >= _tradingSettings.MaxPositionsPerSector)
                return (false, $"Max positions per sector ({_tradingSettings.MaxPositionsPerSector}) reached for {sector}");
        }

        return (true, string.Empty);
    }

    public decimal CalculatePositionSize(decimal accountSize, decimal riskPercent,
        decimal entryPrice, decimal stopLossPrice)
    {
        if (entryPrice == 0 || stopLossPrice == 0) return 0;

        var stopLossPercent = Math.Abs(entryPrice - stopLossPrice) / entryPrice;
        if (stopLossPercent == 0) return 0;

        return accountSize * riskPercent / stopLossPercent;
    }

    /// <summary>
    /// 모든 계좌의 PnL을 업데이트한다.
    /// 각 계좌의 브로커에서 실제 잔고를 가져와 독립적으로 RiskState를 계산한다.
    /// </summary>
    public async Task UpdateDailyPnLAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var tradeRepo = scope.ServiceProvider.GetRequiredService<ITradeRepository>();

        var settings = await settingsRepo.GetAsync(ct);
        var allOpenPositions = await tradeRepo.GetOpenPositionsAsync(ct);
        var accounts = await _accountManager.GetAllAccountsAsync(ct);

        if (accounts.Count == 0)
        {
            // 계좌 없음: 기본 상태 업데이트
            UpdateFallbackState(settings, allOpenPositions);
            return;
        }

        decimal totalPnL = 0;
        decimal totalAccountSize = 0;

        foreach (var account in accounts.Where(a => a.IsEnabled))
        {
            // 이 계좌 소속 포지션만 필터링.
            // AccountId == 0은 레거시(마이그레이션 전) 데이터 — 첫 번째 활성 계좌 소속으로 간주.
            var accountPositions = allOpenPositions
                .Where(p => p.AccountId == account.Id || p.AccountId == 0)
                .ToList();

            // 계좌별 잔고 조회 (브로커 API 호출)
            decimal accountSize = settings.AccountSize; // 기본값
            decimal dailyPnL = 0;
            bool hasBrokerPnL = false;

            try
            {
                var broker = await _accountManager.GetBrokerServiceForAccountAsync(account.Id, ct);
                if (broker != null)
                {
                    var brokerAccount = await broker.GetAccountAsync(ct);
                    if (brokerAccount != null)
                    {
                        accountSize = brokerAccount.TotalEquity > 0
                            ? brokerAccount.TotalEquity
                            : settings.AccountSize;
                        dailyPnL = brokerAccount.DailyPnL;
                        hasBrokerPnL = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not fetch account info for risk calc, using fallback for [{Id}]",
                    account.Id);
            }

            // 포지션 기반 미실현 손익 계산 (이 계좌 소속 포지션만 사용)
            var positionPnL = accountPositions.Sum(p => p.UnrealizedPnL);
            // hasBrokerPnL 플래그로 fallback 여부 결정:
            // dailyPnL != 0 비교는 브로커가 정상적으로 0.0을 반환할 때도 positionPnL로
            // 잘못 교체하는 버그를 유발한다. 브로커 응답 여부만으로 판단해야 한다.
            var effectivePnL = hasBrokerPnL ? dailyPnL : positionPnL;
            var pnlPercent = accountSize > 0 ? effectivePnL / accountSize : 0;

            var sectorCounts = accountPositions
                .GroupBy(p => p.Sector)
                .ToDictionary(g => g.Key, g => g.Count());

            var accountRiskState = new RiskState
            {
                DailyPnL = effectivePnL,
                DailyPnLPercent = pnlPercent,
                IsTradingHalted = pnlPercent <= -_tradingSettings.DailyLossLimitPercent,
                OpenPositionCount = accountPositions.Count,
                PositionsPerSector = sectorCounts,
                LastUpdated = DateTime.UtcNow
            };

            _accountRiskStates[account.Id] = accountRiskState;

            if (accountRiskState.IsTradingHalted)
            {
                _logger.LogWarning("[Account {Id}] TRADING HALTED: Daily loss {PnL:P2} exceeds limit {Limit:P2}",
                    account.Id, pnlPercent, -_tradingSettings.DailyLossLimitPercent);
            }

            totalPnL += effectivePnL;
            totalAccountSize += accountSize;
        }

        // 포트폴리오 전체 리스크 집계 (모든 계좌 합산)
        var totalPnLPercent = totalAccountSize > 0 ? totalPnL / totalAccountSize : 0;
        var allSectorCounts = allOpenPositions
            .GroupBy(p => p.Sector)
            .ToDictionary(g => g.Key, g => g.Count());

        _portfolioRiskState = new RiskState
        {
            DailyPnL = totalPnL,
            DailyPnLPercent = totalPnLPercent,
            IsTradingHalted = totalPnLPercent <= -_tradingSettings.DailyLossLimitPercent,
            OpenPositionCount = allOpenPositions.Count,
            PositionsPerSector = allSectorCounts,
            LastUpdated = DateTime.UtcNow
        };
    }

    private void UpdateFallbackState(UserSettings settings, List<Models.Position> openPositions)
    {
        var dailyPnL = openPositions.Sum(p => p.UnrealizedPnL);
        var dailyPnLPercent = settings.AccountSize > 0 ? dailyPnL / settings.AccountSize : 0;

        _fallbackRiskState = new RiskState
        {
            DailyPnL = dailyPnL,
            DailyPnLPercent = dailyPnLPercent,
            IsTradingHalted = dailyPnLPercent <= -_tradingSettings.DailyLossLimitPercent,
            OpenPositionCount = openPositions.Count,
            LastUpdated = DateTime.UtcNow
        };
    }
}

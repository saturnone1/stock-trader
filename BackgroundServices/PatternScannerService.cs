using System.Collections.Concurrent;
using System.Threading.Channels;
using StockTrader.Application.Strategies;
using StockTrader.Application.Trading;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;
using StockTrader.Services.Notification;
using StockTrader.Services.Order;
using StockTrader.Services.Patterns;
using StockTrader.Services.Signal;
using TimeZoneConverter;
using static StockTrader.Services.Indicators.IndicatorService;

namespace StockTrader.BackgroundServices;

public class PatternScannerService : BackgroundService
{
    private const int MaxConsecutiveFailures = 5;
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<string> _symbolChannel;
    private readonly IIndicatorService _indicatorService;
    private readonly INotificationService _notificationService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PatternScannerService> _logger;

    private int _consecutiveFailures = 0;

    /// <summary>
    /// 일봉 패턴은 하루에 한 번만 스캔하면 충분.
    /// symbol → 마지막 스캔한 날짜(ET 기준)를 추적하여 중복 스캔 방지.
    /// 1분봉 스트리밍으로 390회/일 불필요 실행되는 CPU 낭비를 제거.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateOnly> _lastScanDate = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 기준 종목 레짐 캐시: 공급자·날짜별로 한 번만 DB 쿼리.
    /// 채널 기반 구조에서 심볼마다 레짐을 계산하는 N+1 쿼리를 방지한다.
    /// DateOnly 키로 당일 캐시를 보장하고, 다음날 자동 갱신된다.
    /// </summary>
    private MarketRegime? _cachedRegime;
    private DateOnly _regimeCacheDate = DateOnly.MinValue;
    private string? _regimeCacheSymbol;

    public PatternScannerService(
        IServiceScopeFactory scopeFactory,
        Channel<string> symbolChannel,
        IIndicatorService indicatorService,
        INotificationService notificationService,
        TimeProvider timeProvider,
        ILogger<PatternScannerService> logger)
    {
        _scopeFactory = scopeFactory;
        _symbolChannel = symbolChannel;
        _indicatorService = indicatorService;
        _notificationService = notificationService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PatternScannerService started");

        try
        {
            await foreach (var symbol in _symbolChannel.Reader.ReadAllAsync(stoppingToken))
            {
                // Circuit breaker: cooldown when too many consecutive failures.
                if (Volatile.Read(ref _consecutiveFailures) >= MaxConsecutiveFailures)
                {
                    _logger.LogWarning(
                        "{Service} entering cooldown after {Failures} consecutive failures. " +
                        "Waiting {Cooldown} before resuming",
                        nameof(PatternScannerService), _consecutiveFailures, CooldownPeriod);

                    await Task.Delay(CooldownPeriod, stoppingToken);
                    Interlocked.Exchange(ref _consecutiveFailures, 0);
                }

                try
                {
                    await RetryHelper.ExecuteWithRetryAsync(
                        () => ScanSymbolAsync(symbol, stoppingToken),
                        _logger,
                        $"PatternScan({symbol})",
                        maxRetries: 3,
                        ct: stoppingToken);

                    Interlocked.Exchange(ref _consecutiveFailures, 0);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Shutdown requested — exit the loop cleanly.
                    break;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _consecutiveFailures);
                    _logger.LogError(ex,
                        "Error scanning patterns for {Symbol} (consecutive failures: {Failures})",
                        symbol, Volatile.Read(ref _consecutiveFailures));
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown path — ReadAllAsync throws when the token is cancelled.
            _logger.LogInformation("PatternScannerService stopping due to cancellation");
        }
        catch (ChannelClosedException ex)
        {
            // The channel was closed by the producer — this is a fatal configuration issue.
            _logger.LogError(ex, "Symbol channel was closed unexpectedly; PatternScannerService is stopping");
        }
    }

    private async Task ScanSymbolAsync(string symbol, CancellationToken ct)
    {
        // 일봉 패턴: 하루에 한 번만 스캔 (ET 날짜 기준)
        // 스트리밍은 1분마다 symbol을 push하지만, 일봉 데이터는 하루에 한 번만 갱신됨
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowEt = TimeZoneInfo.ConvertTimeFromUtc(nowUtc,
            TZConvert.GetTimeZoneInfo("America/New_York"));
        var todayEt = DateOnly.FromDateTime(nowEt);

        if (_lastScanDate.TryGetValue(symbol, out var lastDate) && lastDate == todayEt)
            return; // 오늘 이미 스캔함 — 스킵

        using var scope = _scopeFactory.CreateScope();
        var ohlcvRepo = scope.ServiceProvider.GetRequiredService<IOhlcvRepository>();
        var dataFeedFactory = scope.ServiceProvider.GetRequiredService<IDataFeedServiceFactory>();
        var feedSelection = await dataFeedFactory.SelectAsync(null, ct);
        var regimeSymbol = DataProviderCatalog.RegimeBenchmarkSymbol(feedSelection.Source);
        var patternDetection = scope.ServiceProvider.GetRequiredService<PatternDetectionService>();
        var signalService = scope.ServiceProvider.GetRequiredService<ISignalService>();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var signalStore = scope.ServiceProvider.GetRequiredService<IPatternSignalStore>();

        var bars = await ohlcvRepo.GetBarsAsync(symbol, TimeFrame.Daily,
            nowUtc.AddDays(-StrategyEvaluationPolicy.LiveDailySignalLookbackDays), nowUtc, ct);

        if (bars.Count < StrategyEvaluationPolicy.LiveScannerMinimumBars)
        {
            _logger.LogDebug(
                "Skipping {Symbol}: only {Count} daily bars (need >= {Minimum})",
                symbol,
                bars.Count,
                StrategyEvaluationPolicy.LiveScannerMinimumBars);
            return;
        }

        // 스캔 완료 기록 (데이터 로드 후, 결과와 무관하게)
        _lastScanDate[symbol] = todayEt;

        // 공급자 또는 날짜가 달라지면 기준 종목 레짐을 다시 계산한다.
        if (_cachedRegime is null
            || _regimeCacheDate != todayEt
            || !string.Equals(_regimeCacheSymbol, regimeSymbol, StringComparison.OrdinalIgnoreCase))
        {
            _cachedRegime = await ComputeRegimeAsync(ohlcvRepo, regimeSymbol, ct);
            _regimeCacheDate = todayEt;
            _regimeCacheSymbol = regimeSymbol;
        }
        var regime = _cachedRegime;
        if (regime is null)
        {
            _logger.LogWarning("Regime cache is null for {Symbol}, skipping scan", symbol);
            return;
        }
        _logger.LogDebug("Scanning {Symbol}: {Count} daily bars, regime={Regime}",
            symbol, bars.Count, regime.RegimeLabel);
        var signals = await patternDetection.ScanSymbolAsync(symbol, bars.ToArray(), regime, ct);

        if (signals.Count == 0)
        {
            _logger.LogDebug("No signals for {Symbol}", symbol);
            return;
        }

        _logger.LogInformation("Detected {Count} signal(s) for {Symbol}: {Patterns}",
            signals.Count, symbol, string.Join(", ", signals.Select(s => s.PatternType)));

        // Batch insert: 단일 SaveChangesAsync로 모든 신호를 한 번에 저장
        // 기존: N회 AddSignalAsync (각각 SaveChangesAsync) → 개선: 1회 AddSignalsBatchAsync
        await signalStore.AddSignalsBatchAsync(signals, ct);

        var recommendations = await signalService.EvaluateSignalsAsync(signals, ct);

        foreach (var rec in recommendations)
            await orderService.PlaceOrderAsync(rec, ct);
    }

    private async Task<MarketRegime> ComputeRegimeAsync(
        IOhlcvRepository ohlcvRepo,
        string regimeSymbol,
        CancellationToken ct)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        // SMA200에는 최소 200개 daily bars 필요 — 영업일 기준 ~280일(공휴일 감안)이므로 400일치 조회
        var regimeBars = await ohlcvRepo.GetBarsAsync(regimeSymbol, TimeFrame.Daily,
            nowUtc.AddDays(-StrategyEvaluationPolicy.RegimeLookbackCalendarDays), nowUtc, ct);

        var regime = new MarketRegime { AsOf = nowUtc };

        _logger.LogDebug(
            "ComputeRegime: {Symbol} daily bars count = {Count}",
            regimeSymbol,
            regimeBars.Count);

        if (regimeBars.Count >= StrategyEvaluationPolicy.RegimeTrendBars)
        {
            var closes = ExtractCloses(regimeBars.ToArray());
            var sma200 = _indicatorService.SMA(
                closes, StrategyEvaluationPolicy.RegimeTrendBars);
            regime.SpyPrice = closes[^1];
            regime.Spy200Ma = sma200[^1];
            regime.SpyAbove200Ma = regime.SpyPrice > regime.Spy200Ma;
            regime.RegimeLabel = regime.SpyAbove200Ma ? "강세" : "약세";
        }

        return regime;
    }
}

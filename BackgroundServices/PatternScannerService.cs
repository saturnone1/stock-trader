using System.Threading.Channels;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;
using StockTrader.Services.Notification;
using StockTrader.Services.Order;
using StockTrader.Services.Patterns;
using StockTrader.Services.Signal;

namespace StockTrader.BackgroundServices;

public class PatternScannerService : BackgroundService
{
    private const int MaxConsecutiveFailures = 5;
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<string> _symbolChannel;
    private readonly IIndicatorService _indicatorService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PatternScannerService> _logger;

    private int _consecutiveFailures = 0;

    public PatternScannerService(
        IServiceScopeFactory scopeFactory,
        Channel<string> symbolChannel,
        IIndicatorService indicatorService,
        INotificationService notificationService,
        ILogger<PatternScannerService> logger)
    {
        _scopeFactory = scopeFactory;
        _symbolChannel = symbolChannel;
        _indicatorService = indicatorService;
        _notificationService = notificationService;
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
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _logger.LogWarning(
                        "{Service} entering cooldown after {Failures} consecutive failures. " +
                        "Waiting {Cooldown} before resuming",
                        nameof(PatternScannerService), _consecutiveFailures, CooldownPeriod);

                    await Task.Delay(CooldownPeriod, stoppingToken);
                    _consecutiveFailures = 0;
                }

                try
                {
                    await RetryHelper.ExecuteWithRetryAsync(
                        () => ScanSymbolAsync(symbol, stoppingToken),
                        _logger,
                        $"PatternScan({symbol})",
                        maxRetries: 3,
                        ct: stoppingToken);

                    _consecutiveFailures = 0;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Shutdown requested — exit the loop cleanly.
                    break;
                }
                catch (Exception ex)
                {
                    _consecutiveFailures++;
                    _logger.LogError(ex,
                        "Error scanning patterns for {Symbol} (consecutive failures: {Failures})",
                        symbol, _consecutiveFailures);
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
        using var scope = _scopeFactory.CreateScope();
        var ohlcvRepo = scope.ServiceProvider.GetRequiredService<IOhlcvRepository>();
        var dataFeedFactory = scope.ServiceProvider.GetRequiredService<IDataFeedServiceFactory>();
        var dataFeed = await dataFeedFactory.GetServiceAsync(ct);
        var patternDetection = scope.ServiceProvider.GetRequiredService<PatternDetectionService>();
        var signalService = scope.ServiceProvider.GetRequiredService<ISignalService>();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var tradeRepo = scope.ServiceProvider.GetRequiredService<ITradeRepository>();

        var bars = await ohlcvRepo.GetBarsAsync(symbol, TimeFrame.Daily,
            DateTime.UtcNow.AddYears(-1), DateTime.UtcNow, ct);

        if (bars.Count < 20) return;

        var regime = await ComputeRegimeAsync(ohlcvRepo, ct);
        var signals = await patternDetection.ScanSymbolAsync(symbol, bars.ToArray(), regime, ct);

        if (signals.Count == 0) return;

        foreach (var signal in signals)
            await tradeRepo.AddSignalAsync(signal, ct);

        var recommendations = await signalService.EvaluateSignalsAsync(signals, ct);

        foreach (var rec in recommendations)
            await orderService.PlaceOrderAsync(rec, ct);
    }

    private async Task<MarketRegime> ComputeRegimeAsync(IOhlcvRepository ohlcvRepo,
        CancellationToken ct)
    {
        var spyBars = await ohlcvRepo.GetBarsAsync("SPY", TimeFrame.Daily,
            DateTime.UtcNow.AddDays(-250), DateTime.UtcNow, ct);

        var regime = new MarketRegime { AsOf = DateTime.UtcNow };

        if (spyBars.Count >= 200)
        {
            var closes = spyBars.Select(b => b.Close).ToArray();
            var sma200 = _indicatorService.SMA(closes, 200);
            regime.SpyPrice = closes[^1];
            regime.Spy200Ma = sma200[^1];
            regime.SpyAbove200Ma = regime.SpyPrice > regime.Spy200Ma;
            regime.RegimeLabel = regime.SpyAbove200Ma ? "강세" : "약세";
        }

        return regime;
    }
}

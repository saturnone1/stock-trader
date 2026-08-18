using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using StockTrader.Application.MarketData;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Notification;

namespace StockTrader.Services.Streaming;

/// <summary>Alpaca 콜백을 제한된 버퍼에 넣고 저장 성공 후에만 스캐너에 공개합니다.</summary>
public sealed class RealtimeBarIngestionBuffer : IRealtimeBarIngestionBuffer
{
    private readonly IRealtimeBarBatchSink _sink;
    private readonly IStreamingStatusService _streamingStatus;
    private readonly INotificationService _notifications;
    private readonly StreamingSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RealtimeBarIngestionBuffer> _logger;
    private readonly Channel<OhlcvBar> _buffer;
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private readonly List<OhlcvBar> _pendingBatch = [];
    private readonly ConcurrentDictionary<string, decimal> _previousClose =
        new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _acceptBars;

    public RealtimeBarIngestionBuffer(
        IRealtimeBarBatchSink sink,
        IStreamingStatusService streamingStatus,
        INotificationService notifications,
        IOptions<StreamingSettings> settings,
        TimeProvider timeProvider,
        ILogger<RealtimeBarIngestionBuffer> logger)
    {
        _sink = sink;
        _streamingStatus = streamingStatus;
        _notifications = notifications;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _buffer = Channel.CreateBounded<OhlcvBar>(
            new BoundedChannelOptions(_settings.BufferCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                AllowSynchronousContinuations = false
            });
    }

    public void StartAccepting() => _acceptBars = true;

    public void RejectNewBars() => _acceptBars = false;

    public async Task StopAcceptingAsync()
    {
        await _processingLock.WaitAsync(CancellationToken.None);
        try
        {
            _acceptBars = false;
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public async Task ProcessAsync(OhlcvBar bar)
    {
        await _processingLock.WaitAsync();
        try
        {
            if (!_acceptBars)
                return;

            _streamingStatus.MarkActive();
            await _buffer.Writer.WriteAsync(bar);

            var previousClose = _previousClose.GetValueOrDefault(bar.Symbol, bar.Open);
            var change = bar.Close - previousClose;
            var changePercent = previousClose != 0 ? change / previousClose * 100 : 0;
            _notifications.PublishPriceUpdate(new PriceUpdate(
                bar.Symbol,
                bar.Close,
                change,
                changePercent,
                bar.Volume,
                bar.Timestamp));
            _notifications.PublishBarUpdate(bar.Symbol);
            _previousClose[bar.Symbol] = bar.Close;
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public async Task<bool> FlushAsync(CancellationToken ct = default)
    {
        await _flushLock.WaitAsync(ct);
        try
        {
            while (true)
            {
                if (_pendingBatch.Count == 0)
                {
                    while (_buffer.Reader.TryRead(out var bar))
                        _pendingBatch.Add(bar);
                }

                if (_pendingBatch.Count == 0)
                    return true;

                await _sink.PersistAndPublishAsync(_pendingBatch, ct);
                _logger.LogDebug(
                    "Bar flush: persisted and published {Count} bars",
                    _pendingBatch.Count);
                _pendingBatch.Clear();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error flushing {Count} streaming bars; batch retained for retry",
                _pendingBatch.Count);
            return false;
        }
        finally
        {
            _flushLock.Release();
        }
    }

    public async Task RunFlushLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_settings.BarFlushIntervalSeconds),
            _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await FlushAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await FlushAsync(CancellationToken.None);
        }
    }

    public void Complete() => _buffer.Writer.TryComplete();
}

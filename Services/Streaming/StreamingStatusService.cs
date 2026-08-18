using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Domain.MarketData;

namespace StockTrader.Services.Streaming;

public sealed class StreamingStatusService(
    TimeProvider timeProvider,
    IOptions<StreamingSettings> settings) : IStreamingStatusService
{
    private readonly TimeSpan _stalenessWindow =
        TimeSpan.FromSeconds(settings.Value.StatusStalenessSeconds);

    private volatile bool _isActive;
    private volatile bool _isConnected;
    private volatile bool _isReconnecting;
    private DateTime? _lastBarReceivedUtc;
    private readonly object _lock = new();

    public bool IsStreamingActive
    {
        get
        {
            if (!_isActive) return false;

            lock (_lock)
            {
                if (_lastBarReceivedUtc is null) return false;
                if (timeProvider.GetUtcNow().UtcDateTime - _lastBarReceivedUtc.Value
                    > _stalenessWindow)
                {
                    _isActive = false;
                    return false;
                }
                return true;
            }
        }
    }

    public bool IsReconnecting => _isReconnecting;

    public DataSource? ActiveSource =>
        IsStreamingActive ? DataSource.Alpaca : null;

    public DataSource? ConnectedSource =>
        _isConnected ? DataSource.Alpaca : null;

    public DateTime? LastBarReceivedUtc
    {
        get { lock (_lock) { return _lastBarReceivedUtc; } }
    }

    public void MarkActive()
    {
        lock (_lock)
        {
            _isConnected = true;
            _lastBarReceivedUtc = timeProvider.GetUtcNow().UtcDateTime;
            _isActive = true;
            _isReconnecting = false;
        }
    }

    public void MarkInactive()
    {
        lock (_lock)
        {
            _isConnected = false;
            _isActive = false;
            _isReconnecting = false;
        }
    }

    public void MarkConnected()
    {
        lock (_lock)
        {
            _isConnected = true;
            _isReconnecting = false;
        }
    }

    public void MarkReconnecting()
    {
        lock (_lock)
        {
            _isConnected = false;
            _isActive = false;
            _isReconnecting = true;
        }
    }
}

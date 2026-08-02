namespace StockTrader.Services.Streaming;

public class StreamingStatusService : IStreamingStatusService
{
    private static readonly TimeSpan StalenessWindow = TimeSpan.FromMinutes(3);

    private volatile bool _isActive;
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
                if (DateTime.UtcNow - _lastBarReceivedUtc.Value > StalenessWindow)
                {
                    _isActive = false;
                    return false;
                }
                return true;
            }
        }
    }

    public bool IsReconnecting => _isReconnecting;

    public DateTime? LastBarReceivedUtc
    {
        get { lock (_lock) { return _lastBarReceivedUtc; } }
    }

    public void MarkActive(DateTime receivedUtc)
    {
        lock (_lock)
        {
            _lastBarReceivedUtc = receivedUtc;
            _isActive = true;
            _isReconnecting = false;
        }
    }

    public void MarkInactive()
    {
        lock (_lock)
        {
            _isActive = false;
            _isReconnecting = false;
        }
    }

    public void MarkReconnecting()
    {
        lock (_lock)
        {
            _isActive = false;
            _isReconnecting = true;
        }
    }
}

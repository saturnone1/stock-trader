using System.Collections.Concurrent;
using StockTrader.Domain.MarketData;
using StockTrader.Models;

namespace StockTrader.Services.Patterns;

/// <summary>프로세스 수명 동안 완료 일봉 스캔과 기준 종목 국면 캐시를 소유합니다.</summary>
public sealed class LivePatternScanState
{
    private readonly ConcurrentDictionary<(DataSource Source, string Symbol), DateOnly>
        _completedScans = new();
    private readonly SemaphoreSlim _regimeLock = new(1, 1);
    private MarketRegime? _regime;
    private DateOnly _regimeDate = DateOnly.MinValue;
    private string? _regimeSymbol;

    public bool WasScanned(string symbol, DataSource source, DateOnly date) =>
        _completedScans.TryGetValue((source, symbol), out var completedAt)
        && completedAt == date;

    public void MarkScanned(string symbol, DataSource source, DateOnly date) =>
        _completedScans[(source, symbol)] = date;

    public async Task<MarketRegime> GetRegimeAsync(
        string symbol,
        DateOnly date,
        Func<Task<MarketRegime>> factory,
        CancellationToken ct)
    {
        if (Matches(symbol, date))
            return _regime!;

        await _regimeLock.WaitAsync(ct);
        try
        {
            if (Matches(symbol, date))
                return _regime!;

            _regime = await factory();
            _regimeDate = date;
            _regimeSymbol = symbol;
            return _regime;
        }
        finally
        {
            _regimeLock.Release();
        }
    }

    private bool Matches(string symbol, DateOnly date) =>
        _regime is not null
        && _regimeDate == date
        && string.Equals(_regimeSymbol, symbol, StringComparison.OrdinalIgnoreCase);
}

using StockTrader.Application.Backtesting;
using StockTrader.Models;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 한 백테스트 실행에서 커스텀 전략별 상태와 종목별 재진입 대기를 소유합니다.
/// 처리기는 전략명으로 상태를 조회할 뿐 저장 구조나 상태 전이 규칙을 복제하지 않습니다.
/// </summary>
internal sealed class BacktestStrategyRuntimeRegistry
{
    private readonly Dictionary<string, BacktestStrategyRuntime> _runtimes;
    private readonly Dictionary<string, ICustomStrategyDetector> _detectorsByName;
    private readonly Dictionary<string, OhlcvBar[]>? _referenceData;
    private readonly Dictionary<string, int> _reentryCooldowns =
        new(StringComparer.OrdinalIgnoreCase);

    public BacktestStrategyRuntimeRegistry(
        IEnumerable<IPatternDetector> detectors,
        IReadOnlyDictionary<string, PreparedSymbolData> symbolData,
        decimal initialCapital)
    {
        _detectorsByName = detectors
            .OfType<ICustomStrategyDetector>()
            .GroupBy(detector => detector.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _runtimes = _detectorsByName.ToDictionary(
            pair => pair.Key,
            pair => new BacktestStrategyRuntime
            {
                Detector = pair.Value,
                CircuitBreaker = pair.Value.Strategy.CircuitBreaker,
                Reentry = pair.Value.Strategy.Reentry,
                Portfolio = pair.Value.Strategy.PortfolioRules,
                RealizedEquity = initialCapital,
                PeakEquity = initialCapital
            },
            StringComparer.OrdinalIgnoreCase);

        if (_detectorsByName.Count > 0)
        {
            _referenceData = symbolData.ToDictionary(
                pair => pair.Key.ToUpperInvariant(),
                pair => pair.Value.Bars,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public BacktestStrategyRuntime? Find(string? strategyName) =>
        !string.IsNullOrWhiteSpace(strategyName)
        && _runtimes.TryGetValue(strategyName, out var runtime)
            ? runtime
            : null;

    public ICustomStrategyDetector? FindDetector(string? strategyName) =>
        !string.IsNullOrWhiteSpace(strategyName)
        && _detectorsByName.TryGetValue(strategyName, out var detector)
            ? detector
            : null;

    public int? GetCooldownUntil(string? strategyName, string symbol)
    {
        if (string.IsNullOrWhiteSpace(strategyName))
            return null;

        return _reentryCooldowns.TryGetValue(Key(strategyName, symbol), out var blockedUntil)
            ? blockedUntil
            : null;
    }

    public void BeginStep(DateTime asOf, DateOnly tradingDay)
    {
        if (_referenceData != null)
        {
            foreach (var detector in _detectorsByName.Values)
                detector.SetReferenceData(_referenceData, asOf);
        }

        foreach (var runtime in _runtimes.Values)
        {
            if (runtime.LastEntryDate != tradingDay)
                runtime.DailyEntryCount = 0;
        }
    }

    public void RegisterEntry(string? strategyName, DateOnly tradingDay)
    {
        var runtime = Find(strategyName);
        if (runtime == null) return;

        runtime.DailyEntryCount++;
        runtime.LastEntryDate = tradingDay;
    }

    public void RegisterClosedTrade(
        string? strategyName,
        string symbol,
        int currentBarIndex,
        int currentTimelineStep,
        TradeRecord trade)
    {
        var runtime = Find(strategyName);
        if (runtime == null || string.IsNullOrWhiteSpace(strategyName)) return;

        BacktestStrategyTransitionPolicy.RegisterClosedTrade(
            Key(strategyName, symbol),
            currentBarIndex,
            currentTimelineStep,
            trade,
            runtime,
            _reentryCooldowns);
    }

    public void ApplyRealizedTrade(TradeRecord trade)
    {
        var runtime = Find(trade.CustomPatternName);
        if (runtime == null) return;

        runtime.RealizedEquity += trade.PnL;
        if (runtime.RealizedEquity > runtime.PeakEquity)
            runtime.PeakEquity = runtime.RealizedEquity;

        if (runtime.CircuitBreaker.MaxDrawdownPercent <= 0 || runtime.PeakEquity <= 0)
            return;

        var drawdownPercent = (runtime.PeakEquity - runtime.RealizedEquity)
            / runtime.PeakEquity * 100m;
        if (drawdownPercent >= runtime.CircuitBreaker.MaxDrawdownPercent)
            runtime.CircuitBreakerTripped = true;
    }

    private static string Key(string strategyName, string symbol) =>
        $"{strategyName}|{symbol}";
}

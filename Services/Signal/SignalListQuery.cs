using StockTrader.Application.Signals;
using StockTrader.Application.Statistics;
using StockTrader.Application.Trading;

namespace StockTrader.Services.Signal;

public sealed class SignalListQuery(
    IPatternSignalStore signals,
    IPatternStatisticsQuery statistics)
    : ISignalListQuery
{
    public async Task<SignalListSnapshot> GetAsync(
        SignalBrowseRequest request,
        CancellationToken ct = default)
    {
        var signalsTask = signals.GetActiveSignalsAsync(ct);
        var statisticsTask = statistics.GetAllAsync(ct);
        await Task.WhenAll(signalsTask, statisticsTask);

        var candidates = (await signalsTask).Select(signal => new BrowsableSignal(
            signal.Id,
            signal.Symbol,
            signal.PatternType,
            signal.EntryPrice,
            signal.StopLossPrice,
            signal.TargetPrice,
            signal.Confidence,
            signal.Details,
            signal.DetectedAt));

        return SignalListPolicy.Evaluate(
            candidates,
            await statisticsTask,
            request);
    }
}

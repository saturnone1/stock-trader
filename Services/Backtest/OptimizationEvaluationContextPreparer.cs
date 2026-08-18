using Microsoft.Extensions.Options;
using StockTrader.Application.Backtesting;
using StockTrader.Application.Optimization;
using StockTrader.Configuration;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

/// <summary>최적화의 외부 데이터 의존성을 준비된 평가 컨텍스트로 변환합니다.</summary>
public sealed class OptimizationEvaluationContextPreparer
    : IOptimizationEvaluationContextPreparer
{
    private readonly IDataFeedServiceFactory _dataFeeds;
    private readonly ICustomStrategyDetectorFactory _detectors;
    private readonly BacktestDataPreparer _dataPreparer;
    private readonly BacktestRegimeMapBuilder _regimes;
    private readonly TradingSettings _tradingSettings;
    private readonly PatternSettings _patternSettings;

    public OptimizationEvaluationContextPreparer(
        IDataFeedServiceFactory dataFeeds,
        ICustomStrategyDetectorFactory detectors,
        BacktestDataPreparer dataPreparer,
        BacktestRegimeMapBuilder regimes,
        IOptions<TradingSettings> tradingSettings,
        IOptions<PatternSettings> patternSettings)
    {
        _dataFeeds = dataFeeds;
        _detectors = detectors;
        _dataPreparer = dataPreparer;
        _regimes = regimes;
        _tradingSettings = tradingSettings.Value;
        _patternSettings = patternSettings.Value;
    }

    public async Task<OptimizationPreparationResult> PrepareAsync(
        OptimizeRequest request,
        CancellationToken ct)
    {
        var feedSelection = await _dataFeeds.SelectAsync(request.DataSource, ct);
        var regimeSymbol = DataProviderCatalog.RegimeBenchmarkSymbol(feedSelection.Source);
        var regimes = await _regimes.BuildAsync(
            feedSelection.Service,
            request.From,
            request.To,
            regimeSymbol,
            ct);
        if (regimes is null)
        {
            return OptimizationPreparationResult.Failed(
                OptimizationPreparationFailure.RegimeDataUnavailable,
                $"레짐 맵 빌드 실패 — {regimeSymbol} 데이터를 확인하세요");
        }

        var detector = _detectors.Create(request.BasePattern);
        var symbols = OptimizationDataPreparationPolicy.ResolveSymbols(
            request,
            BacktestDetectorMetadata.CollectReferenceSymbols([detector]));
        var dataByTimeFrame = new Dictionary<TimeFrame,
            IReadOnlyDictionary<string, PreparedSymbolData>>();

        foreach (var timeFrame in OptimizationDataPreparationPolicy.ResolveTimeFrames(request))
        {
            var prepared = await _dataPreparer.PrepareAsync(
                feedSelection.Service,
                symbols,
                timeFrame,
                request.From,
                request.To,
                _patternSettings.CumulativeRsi2,
                _patternSettings.Tqqq200Sma,
                ct);
            if (prepared.HasData)
                dataByTimeFrame[timeFrame] = prepared.Symbols;
        }

        if (dataByTimeFrame.Count == 0)
        {
            return OptimizationPreparationResult.Failed(
                OptimizationPreparationFailure.NoUsableSymbolData,
                "유효한 심볼 데이터 없음 — 데이터 피드/심볼을 확인하세요");
        }

        var defaultData = dataByTimeFrame.TryGetValue(
            request.TimeFrame, out var requestedData)
            ? requestedData
            : dataByTimeFrame.Values.First();
        var risk = new OptimizationRiskParameters(
            _tradingSettings.RiskPerTradePercent,
            _tradingSettings.DailyLossLimitPercent,
            _tradingSettings.MaxTotalPositions,
            _tradingSettings.MaxPositionsPerSector);

        return OptimizationPreparationResult.Success(
            new OptimizationEvaluationContext(
                request,
                dataByTimeFrame,
                defaultData,
                regimes,
                risk));
    }
}

using StockTrader.Application.Strategies;
using StockTrader.Application.StrategyPreview;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.StrategyPreview;

/// <summary>
/// 미리보기 요청의 전략 컴파일, 시세 준비와 순수 시뮬레이션 호출을 조정합니다.
/// HTTP 상태와 JSON 모양은 소유하지 않습니다.
/// </summary>
public sealed class PatternPreviewService : IPatternPreviewService
{
    private readonly IOhlcvRepository _ohlcv;
    private readonly IDataFeedServiceFactory _dataFeeds;
    private readonly IIndicatorService _indicators;
    private readonly ICustomStrategyDetectorFactory _runtimes;
    private readonly PatternPreviewSimulationEngine _simulation;
    private readonly TimeProvider _timeProvider;

    public PatternPreviewService(
        IOhlcvRepository ohlcv,
        IDataFeedServiceFactory dataFeeds,
        IIndicatorService indicators,
        ICustomStrategyDetectorFactory runtimes,
        PatternPreviewSimulationEngine simulation,
        TimeProvider timeProvider)
    {
        _ohlcv = ohlcv;
        _dataFeeds = dataFeeds;
        _indicators = indicators;
        _runtimes = runtimes;
        _simulation = simulation;
        _timeProvider = timeProvider;
    }

    public async Task<PatternPreviewOutcome> PreviewAsync(
        PatternPreviewQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var symbol = MarketSymbolPolicy.Normalize(query.Symbol);
        if (string.IsNullOrWhiteSpace(symbol))
            return PatternPreviewOutcome.Invalid("미리보기 종목을 입력하세요.");
        if (query.Pattern is null)
            return PatternPreviewOutcome.Invalid("미리보기 패턴 정의가 필요합니다.");

        var compilation = StrategyCompiler.Compile(query.Pattern);
        if (!compilation.IsValid)
        {
            return PatternPreviewOutcome.Invalid(
                compilation.Errors[0], compilation.Errors);
        }

        var strategy = compilation.Strategy!;
        var isIntraday = TimeFrameCatalog.IsIntraday(query.TimeFrame);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var requestedTo = query.To ?? nowUtc;
        var dataTo = isIntraday
            ? requestedTo.ToUniversalTime()
            : requestedTo.Date.AddDays(1);
        var requestedFrom = query.From
            ?? PreviewTimeFramePolicy.Get(query.TimeFrame).DefaultFrom(dataTo);
        var dataFrom = isIntraday
            ? requestedFrom.ToUniversalTime()
            : requestedFrom.Date;
        if (dataFrom >= dataTo)
        {
            return PatternPreviewOutcome.Invalid(
                "조회 시작 시점은 종료 시점보다 앞서야 합니다.");
        }

        var timeFramePolicy = PreviewTimeFramePolicy.Get(query.TimeFrame);
        if (dataTo - dataFrom > timeFramePolicy.MaximumRange)
        {
            return PatternPreviewOutcome.Invalid(
                $"{TimeFrameCatalog.DisplayName(query.TimeFrame)}은(는) 최대 "
                + $"{DisplayRange(timeFramePolicy.MaximumRange)}까지 한 번에 조회할 수 있습니다.");
        }

        var feedSelection = await _dataFeeds.SelectAsync(null, ct);
        var regimeSymbol = DataProviderCatalog.RegimeBenchmarkSymbol(feedSelection.Source);
        var queryFrom = dataFrom - timeFramePolicy.WarmupRange;
        var allBars = await LoadBarsAsync(
            symbol, query.TimeFrame, queryFrom, dataTo, ct);
        var expectedLatest = dataTo < nowUtc ? dataTo.AddDays(-1) : nowUtc;
        var needsFetch = allBars.Length < StrategyEvaluationPolicy.MinimumWarmupBars
            || allBars[0].Timestamp > queryFrom + timeFramePolicy.CoverageTolerance
            || allBars[^1].Timestamp < expectedLatest - timeFramePolicy.CoverageTolerance;

        if (needsFetch)
        {
            try
            {
                var fetched = await feedSelection.Service.GetHistoricalBarsAsync(
                    symbol, query.TimeFrame, queryFrom, dataTo, ct);
                if (fetched.Count > 0) await _ohlcv.AddBarsAsync(fetched, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return PatternPreviewOutcome.ProviderError(
                    $"{symbol} {TimeFrameCatalog.DisplayName(query.TimeFrame)}을 현재 데이터 제공자에서 가져오지 못했습니다.");
            }

            allBars = await LoadBarsAsync(
                symbol, query.TimeFrame, queryFrom, dataTo, ct);
        }

        if (allBars.Length < StrategyEvaluationPolicy.MinimumWarmupBars)
        {
            return PatternPreviewOutcome.NotFound(
                $"{symbol} {TimeFrameCatalog.DisplayName(query.TimeFrame)}을 찾을 수 없거나 "
                + "패턴 평가에 필요한 데이터가 부족합니다.");
        }

        var referenceBars = await LoadReferenceBarsAsync(
            strategy,
            query.TimeFrame,
            allBars[0].Timestamp,
            allBars[^1].Timestamp,
            ct);
        var regimeBars = (await _ohlcv.GetBarsAsync(
                regimeSymbol,
                TimeFrame.Daily,
                dataFrom.AddYears(-2),
                allBars[^1].Timestamp.AddDays(1),
                ct))
            .OrderBy(bar => bar.Timestamp)
            .ToArray();
        var runtime = _runtimes.Create(strategy);
        var result = await _simulation.RunAsync(
            new PatternPreviewSimulationInput(
                symbol,
                query.TimeFrame,
                dataFrom,
                dataTo,
                allBars,
                _indicators.ATR(allBars, StrategyEvaluationPolicy.EntryAtrPeriod),
                referenceBars,
                regimeBars,
                runtime),
            ct);

        return result is null
            ? PatternPreviewOutcome.NotFound("선택한 기간에 표시할 시세가 없습니다.")
            : PatternPreviewOutcome.Success(result);
    }

    private async Task<OhlcvBar[]> LoadBarsAsync(
        string symbol,
        TimeFrame timeFrame,
        DateTime from,
        DateTime to,
        CancellationToken ct) =>
        (await _ohlcv.GetBarsAsync(symbol, timeFrame, from, to, ct))
            .OrderBy(bar => bar.Timestamp)
            .GroupBy(bar => bar.Timestamp)
            .Select(group => group.Last())
            .ToArray();

    private async Task<Dictionary<string, OhlcvBar[]>> LoadReferenceBarsAsync(
        CompiledStrategy strategy,
        TimeFrame timeFrame,
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        var result = new Dictionary<string, OhlcvBar[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in strategy.ReferenceSymbols)
        {
            result[symbol] = (await _ohlcv.GetBarsAsync(
                    symbol, timeFrame, from, to.AddDays(1), ct))
                .OrderBy(bar => bar.Timestamp)
                .ToArray();
        }
        return result;
    }

    private static string DisplayRange(TimeSpan range) => range.TotalDays >= 365
        ? $"{Math.Floor(range.TotalDays / 365):N0}년"
        : $"{range.TotalDays:N0}일";
}

using System.Text.Json;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Application.StrategyPreview;
using StockTrader.Application.Strategies;
using StockTrader.Application.Execution;
using StockTrader.Domain.MarketData;
using StockTrader.Domain.Strategies;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Api;

public sealed record PatternPreviewRequest(
    string Symbol,
    CustomPatternDefinition Pattern,
    TimeFrame TimeFrame = TimeFrame.Daily,
    DateTime? From = null,
    DateTime? To = null);

public sealed record PatternPreviewMarker(
    DateTime Date,
    string Type,
    decimal Price,
    decimal? StopPrice = null,
    decimal? TargetPrice = null,
    string? Details = null,
    string? Reason = null);

public static class PatternPreviewEndpoints
{
    public static RouteGroupBuilder MapPatternPreviewApi(this RouteGroupBuilder api)
    {
        api.MapPost("/custom-patterns/preview", PreviewAsync)
            .RequireAuthorization();

        return api;
    }

    private static async Task<IResult> PreviewAsync(
        PatternPreviewRequest request,
        IOhlcvRepository ohlcvRepository,
        IDataFeedServiceFactory dataFeedFactory,
        IIndicatorService indicators,
        ICustomStrategyDetectorFactory customDetectors,
        CancellationToken ct)
    {
        var symbol = (request.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
            return Results.BadRequest(new { error = "미리보기 종목을 입력하세요." });

        if (request.Pattern is null)
            return Results.BadRequest(new { error = "미리보기 패턴 정의가 필요합니다." });

        var compilation = StrategyCompiler.Compile(request.Pattern);
        if (!compilation.IsValid)
            return Results.BadRequest(new { error = compilation.Errors[0], errors = compilation.Errors });
        var strategy = compilation.Strategy!;

        var isIntraday = IsIntraday(request.TimeFrame);
        var requestedTo = request.To ?? DateTime.UtcNow;
        var dataTo = isIntraday ? requestedTo.ToUniversalTime() : requestedTo.Date.AddDays(1);
        var requestedFrom = request.From ?? DefaultFrom(request.TimeFrame, dataTo);
        var dataFrom = isIntraday ? requestedFrom.ToUniversalTime() : requestedFrom.Date;
        if (dataFrom >= dataTo)
            return Results.BadRequest(new { error = "조회 시작 시점은 종료 시점보다 앞서야 합니다." });

        var maximumRange = MaximumRange(request.TimeFrame);
        if (dataTo - dataFrom > maximumRange)
            return Results.BadRequest(new
            {
                error = $"{DisplayTimeFrame(request.TimeFrame)}은(는) 최대 {DisplayRange(maximumRange)}까지 한 번에 조회할 수 있습니다."
            });

        var queryFrom = dataFrom - WarmupRange(request.TimeFrame);
        var allBars = (await ohlcvRepository.GetBarsAsync(
                symbol, request.TimeFrame, queryFrom, dataTo, ct))
            .OrderBy(bar => bar.Timestamp)
            .GroupBy(bar => bar.Timestamp)
            .Select(group => group.Last())
            .ToArray();

        var expectedLatest = dataTo < DateTime.UtcNow ? dataTo.AddDays(-1) : DateTime.UtcNow;
        var tolerance = CoverageTolerance(request.TimeFrame);
        var needsFetch = allBars.Length < 50
            || allBars[0].Timestamp > queryFrom + tolerance
            || allBars[^1].Timestamp < expectedLatest - tolerance;

        if (needsFetch)
        {
            try
            {
                var dataFeed = await dataFeedFactory.GetServiceAsync(ct);
                var fetched = await dataFeed.GetHistoricalBarsAsync(
                    symbol, request.TimeFrame, queryFrom, dataTo, ct);
                if (fetched.Count > 0)
                    await ohlcvRepository.AddBarsAsync(fetched, ct);
            }
            catch (Exception)
            {
                return Results.Json(
                    new { error = $"{symbol} {DisplayTimeFrame(request.TimeFrame)}을 현재 데이터 제공자에서 가져오지 못했습니다." },
                    statusCode: StatusCodes.Status502BadGateway);
            }

            allBars = (await ohlcvRepository.GetBarsAsync(
                    symbol, request.TimeFrame, queryFrom, dataTo, ct))
                .OrderBy(bar => bar.Timestamp)
                .GroupBy(bar => bar.Timestamp)
                .Select(group => group.Last())
                .ToArray();
        }

        if (allBars.Length < 50)
            return Results.NotFound(new { error = $"{symbol} {DisplayTimeFrame(request.TimeFrame)}을 찾을 수 없거나 패턴 평가에 필요한 데이터가 부족합니다." });

        var latest = allBars[^1];

        var detector = customDetectors.Create(strategy);
        var circuitBreaker = strategy.CircuitBreaker;
        var reentry = strategy.Reentry;
        var portfolioRules = strategy.PortfolioRules;
        var referenceBars = await LoadReferenceBarsAsync(
            strategy, request.TimeFrame, allBars[0].Timestamp, latest.Timestamp, ohlcvRepository, ct);
        var spyBars = (await ohlcvRepository.GetBarsAsync(
                "SPY", TimeFrame.Daily, dataFrom.AddYears(-2), latest.Timestamp.AddDays(1), ct))
            .OrderBy(bar => bar.Timestamp)
            .ToArray();

        var markers = new List<PatternPreviewMarker>();
        var matches = new List<PatternPreviewMarker>();
        var warnings = new List<string>();
        var requestedStartIndex = Array.FindIndex(allBars, bar => bar.Timestamp >= dataFrom);
        if (requestedStartIndex < 0)
            return Results.NotFound(new { error = "선택한 기간에 표시할 시세가 없습니다." });
        var requestedBars = allBars.Skip(requestedStartIndex).Where(bar => bar.Timestamp < dataTo).ToArray();
        if (requestedBars.Length == 0)
            return Results.NotFound(new { error = "선택한 기간에 표시할 시세가 없습니다." });
        var displayStartIndex = requestedStartIndex;
        var evaluationStartIndex = Math.Max(49, displayStartIndex);
        OpenPreviewPosition? position = null;
        decimal compoundedReturn = 1m;
        var completedTrades = 0;
        var winningTrades = 0;
        var consecutiveLosses = 0;
        var circuitBreakerUntilIndex = 0;
        var reentryUntilIndex = 0;
        var drawdownTripped = false;
        var peakCompoundedReturn = 1m;
        var currentEntryDay = DateOnly.MinValue;
        var entriesToday = 0;
        var safetyBlockedEntries = 0;
        var exitPolicy = LongPositionExitPolicyCatalog.ForCustom(strategy.Source);

        void Realize(OpenPreviewPosition openPosition, decimal price, int quantity)
        {
            if (quantity > 0)
                openPosition.RealizedPnl += (price - openPosition.EntryPrice) * quantity;
        }

        void Complete(OpenPreviewPosition openPosition, decimal price)
        {
            Realize(openPosition, price, openPosition.CurrentQuantity);
            var cycleReturn = openPosition.InvestedCapital > 0
                ? openPosition.RealizedPnl / openPosition.InvestedCapital
                : 0;
            compoundedReturn *= Math.Max(0m, 1m + cycleReturn * openPosition.AllocationScale);
            completedTrades++;
            if (openPosition.RealizedPnl > 0) winningTrades++;
        }

        foreach (var (refSymbol, bars) in referenceBars)
        {
            if (bars.Length < 50)
                warnings.Add($"참조 종목 {refSymbol} 데이터가 부족해 해당 조건은 미리보기에서 제한될 수 있습니다.");
        }

        for (var index = evaluationStartIndex; index < allBars.Length; index++)
        {
            var current = allBars[index];
            var window = allBars[..(index + 1)];
            detector.SetReferenceData(referenceBars.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Where(bar => bar.Timestamp <= current.Timestamp).ToArray()));

            // NextOpen은 진입 봉 자체의 고가/저가도 실제 보유 구간이므로 즉시 평가한다.
            if (position is not null && index >= position.EntryIndex)
            {
                var currentAtr = indicators.ATR(window, 14).LastOrDefault();
                var strategyExit = detector.ShouldExit(window)
                    ? new StrategyExitInstruction(current.Close, "청산 규칙 충족")
                    : null;
                var result = LongPositionExecutionPolicy.Evaluate(
                    new LongPositionExecutionState(
                        position.EntryPrice,
                        position.StopPrice,
                        position.TargetPrice,
                        position.HighestPrice,
                        position.LowestPrice,
                        position.InitialRisk,
                        position.EntryAtr,
                        position.EntryIndex,
                        position.CurrentQuantity,
                        position.PartialProfitTaken,
                        position.BreakevenApplied,
                        position.TrailingActivated),
                    current,
                    index,
                    currentAtr,
                    exitPolicy,
                    strategyExit);

                position.StopPrice = result.State.StopPrice;
                position.HighestPrice = result.State.HighestPrice;
                position.LowestPrice = result.State.LowestPrice;
                position.CurrentQuantity = result.State.CurrentQuantity;
                position.PartialProfitTaken = result.State.PartialProfitTaken;
                position.BreakevenApplied = result.State.BreakevenApplied;
                position.TrailingActivated = result.State.TrailingActivated;

                foreach (var executionEvent in result.Events)
                {
                    if (executionEvent.Type == PositionExecutionEventType.StopMoved)
                    {
                        markers.Add(new PatternPreviewMarker(
                            current.Timestamp,
                            "STOP_MOVE",
                            executionEvent.Price,
                            Details: executionEvent.Reason));
                    }
                    else if (executionEvent.Type == PositionExecutionEventType.PartialExit)
                    {
                        Realize(position, executionEvent.Price, executionEvent.Quantity);
                        markers.Add(new PatternPreviewMarker(
                            current.Timestamp,
                            "PARTIAL_EXIT",
                            executionEvent.Price,
                            Details: $"보유 수량의 {executionEvent.Quantity * 100m / (executionEvent.Quantity + position.CurrentQuantity):F0}% 매도",
                            Reason: executionEvent.Reason));
                    }
                }

                var policyExit = result.Events.LastOrDefault(item => item.Type == PositionExecutionEventType.Exit);
                string? closeReason = policyExit?.Reason;
                var closePrice = policyExit?.Price ?? current.Close;

                if (closeReason is null && detector.HasScalingRules)
                {
                    var profitPercent = position.EntryPrice > 0
                        ? (current.Close - position.EntryPrice) / position.EntryPrice * 100m
                        : 0;
                    var scaling = detector.CheckScaling(window, profitPercent, position.ScaleCounts);
                    if (scaling is not null)
                    {
                        var amount = Math.Max(1, (int)Math.Round(position.InitialQuantity * scaling.Percent / 100m));
                        if (string.Equals(
                                scaling.Direction,
                                StrategyCatalog.ScalingInDirection,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            var totalCost = position.EntryPrice * position.CurrentQuantity + current.Close * amount;
                            position.InvestedCapital += current.Close * amount;
                            position.CurrentQuantity += amount;
                            position.EntryPrice = totalCost / position.CurrentQuantity;
                            markers.Add(new PatternPreviewMarker(
                                current.Timestamp, StrategyCatalog.ScalingInDirection, current.Close,
                                Details: $"최초 수량의 {scaling.Percent:F0}% 추가 매수 · 새 평균가 {position.EntryPrice:F2}"));
                        }
                        else
                        {
                            var sold = Math.Min(amount, position.CurrentQuantity - 1);
                            if (sold > 0)
                            {
                                Realize(position, current.Close, sold);
                                position.CurrentQuantity -= sold;
                                markers.Add(new PatternPreviewMarker(
                                    current.Timestamp, StrategyCatalog.ScalingOutDirection, current.Close,
                                    Details: $"최초 수량의 {scaling.Percent:F0}% 일부 매도"));
                            }
                        }
                    }
                }

                if (closeReason is not null)
                {
                    Complete(position, closePrice);
                    var wasLoss = position.RealizedPnl < 0;
                    var reentryBars = wasLoss ? reentry.CooldownBarsAfterLoss : reentry.CooldownBarsAfterWin;
                    if (reentryBars > 0) reentryUntilIndex = index + reentryBars + 1;

                    consecutiveLosses = wasLoss ? consecutiveLosses + 1 : 0;
                    if (circuitBreaker.ConsecutiveLossLimit > 0
                        && consecutiveLosses >= circuitBreaker.ConsecutiveLossLimit)
                    {
                        circuitBreakerUntilIndex = index + circuitBreaker.CooldownBars + 1;
                        consecutiveLosses = 0;
                    }
                    peakCompoundedReturn = Math.Max(peakCompoundedReturn, compoundedReturn);
                    if (circuitBreaker.MaxDrawdownPercent > 0 && peakCompoundedReturn > 0
                        && (peakCompoundedReturn - compoundedReturn) / peakCompoundedReturn * 100m >= circuitBreaker.MaxDrawdownPercent)
                        drawdownTripped = true;

                    markers.Add(new PatternPreviewMarker(current.Timestamp, "EXIT", closePrice, Reason: closeReason));
                    position = null;
                }
            }

            var regime = BuildRegime(current.Timestamp, spyBars);
            var signal = await detector.DetectAsync(symbol, window, regime, ct);
            if (signal is null)
                continue;

            matches.Add(new PatternPreviewMarker(
                current.Timestamp,
                "MATCH",
                current.Close,
                signal.StopLossPrice,
                signal.TargetPrice,
                signal.Details));

            if (position is not null)
                continue;

            var signalDay = DateOnly.FromDateTime(current.Timestamp);
            if (signalDay != currentEntryDay)
            {
                currentEntryDay = signalDay;
                entriesToday = 0;
            }
            var entryEligibility = StrategyEntryEligibilityPolicy.Evaluate(
                new StrategyEntryEligibilityRequest(
                    DefaultMaxPositions: 1,
                    StrategyMaxPositions: portfolioRules.MaxTotalPositions,
                    OpenPositionCount: 0,
                    DrawdownBlocked: drawdownTripped,
                    ConsecutiveLossBlocked: index < circuitBreakerUntilIndex,
                    MaxEntriesPerSession: portfolioRules.MaxEntriesPerDay,
                    EntriesThisSession: entriesToday,
                    ReentryBlocked: index < reentryUntilIndex));
            if (!entryEligibility.CanEnter)
            {
                safetyBlockedEntries++;
                continue;
            }

            var entryIndex = index;
            var entryPrice = current.Close;
            var entryDate = current.Timestamp;
            if (string.Equals(
                    strategy.EntryMode,
                    StrategyCatalog.NextOpenEntryMode,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= allBars.Length)
                    continue;
                entryIndex = index + 1;
                entryPrice = allBars[entryIndex].Open;
                entryDate = allBars[entryIndex].Timestamp;
            }

            var fallbackTargetMultiple = strategy.Source.AtrStopMultiplier > 0
                ? strategy.Source.AtrTargetMultiplier / strategy.Source.AtrStopMultiplier
                : 1m;
            var fill = LongEntryFillPolicy.Reprice(
                signal.EntryPrice,
                signal.StopLossPrice,
                signal.TargetPrice,
                entryPrice,
                fallbackTargetMultiple);
            if (fill is null)
                continue;
            var entryAtr = indicators.ATR(window, 14).LastOrDefault();
            position = new OpenPreviewPosition
            {
                EntryIndex = entryIndex,
                EntryPrice = fill.EntryPrice,
                StopPrice = fill.StopPrice,
                TargetPrice = fill.TargetPrice,
                HighestPrice = fill.EntryPrice,
                LowestPrice = fill.EntryPrice,
                InitialRisk = fill.RiskDistance,
                EntryAtr = entryAtr > 0 ? entryAtr : fill.RiskDistance,
                InvestedCapital = fill.EntryPrice * 100,
                AllocationScale = Math.Min(
                    PositionAllocationPolicy.NormalizeScale(signal.AllocationScale),
                    portfolioRules.MaxSinglePositionPercent > 0 ? portfolioRules.MaxSinglePositionPercent / 100m : 1m)
            };
            entriesToday++;
            markers.Add(new PatternPreviewMarker(
                entryDate,
                "ENTRY",
                fill.EntryPrice,
                fill.StopPrice,
                fill.TargetPrice,
                $"{signal.Details} · 매수 비중 {signal.AllocationScale * 100m:F0}%"));
        }

        var visibleBars = allBars.Skip(displayStartIndex).Where(bar => bar.Timestamp < dataTo).Select(bar => new
        {
            date = bar.Timestamp.ToString("O"),
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            bar.Volume
        });
        var displayStart = allBars[displayStartIndex].Timestamp;
        var visibleMarkers = markers.Where(marker => marker.Date >= displayStart && marker.Date < dataTo).ToList();
        var visibleMatches = matches.Where(marker => marker.Date >= displayStart && marker.Date < dataTo).ToList();
        var completedReturnPercent = (compoundedReturn - 1m) * 100m;
        var totalReturnPercent = completedReturnPercent;
        decimal? openPositionReturnPercent = null;
        if (position is not null)
        {
            var lastVisibleClose = allBars.Where(bar => bar.Timestamp < dataTo).Last().Close;
            var openPnl = position.RealizedPnl
                + (lastVisibleClose - position.EntryPrice) * position.CurrentQuantity;
            var openCycleReturn = position.InvestedCapital > 0 ? openPnl / position.InvestedCapital : 0;
            openPositionReturnPercent = openCycleReturn * 100m;
            totalReturnPercent = (compoundedReturn * (1m + openCycleReturn * position.AllocationScale) - 1m) * 100m;
        }

        return Results.Ok(new
        {
            symbol,
            timeFrame = request.TimeFrame.ToString(),
            bars = visibleBars,
            markers = visibleMarkers.Select(marker => new
            {
                date = marker.Date.ToString("O"),
                type = marker.Type,
                marker.Price,
                marker.StopPrice,
                marker.TargetPrice,
                marker.Details,
                marker.Reason
            }),
            matches = visibleMatches.Select(marker => new
            {
                date = marker.Date.ToString("O"),
                marker.Price,
                marker.Details
            }),
            summary = new
            {
                matchCount = visibleMatches.Count,
                entryCount = visibleMarkers.Count(marker => marker.Type == "ENTRY"),
                exitCount = visibleMarkers.Count(marker => marker.Type == "EXIT"),
                scaleInCount = visibleMarkers.Count(marker => marker.Type == StrategyCatalog.ScalingInDirection),
                partialExitCount = visibleMarkers.Count(marker =>
                    marker.Type is StrategyCatalog.ScalingOutDirection or "PARTIAL_EXIT"),
                stopMoveCount = visibleMarkers.Count(marker => marker.Type == "STOP_MOVE"),
                safetyBlockedEntries,
                completedTrades,
                winningTrades,
                winRate = completedTrades > 0 ? (decimal)winningTrades / completedTrades : 0,
                completedReturnPercent,
                totalReturnPercent,
                openPositionReturnPercent,
                openPosition = position is not null,
                from = displayStart.ToString("O"),
                to = allBars.Where(bar => bar.Timestamp < dataTo).Last().Timestamp.ToString("O"),
                requestedFrom = isIntraday ? DisplayMarketTime(dataFrom) : dataFrom.ToString("yyyy-MM-dd"),
                requestedTo = isIntraday ? DisplayMarketTime(dataTo) : dataTo.AddDays(-1).ToString("yyyy-MM-dd"),
                requestedBarCount = requestedBars.Length,
                displayedBarCount = requestedBars.Length
            },
            warnings = warnings.Distinct()
        });
    }

    private static MarketRegime BuildRegime(DateTime timestamp, OhlcvBar[] spyBars)
    {
        var available = spyBars.Where(bar => bar.Timestamp <= timestamp).TakeLast(200).ToArray();
        var price = available.LastOrDefault()?.Close ?? 0;
        var average = available.Length > 0 ? available.Average(bar => bar.Close) : 0;
        return new MarketRegime
        {
            SpyAbove200Ma = available.Length < 200 || price >= average,
            SpyPrice = price,
            Spy200Ma = average,
            RegimeLabel = price >= average ? "Bull" : "Bear",
            AsOf = timestamp
        };
    }

    private static async Task<Dictionary<string, OhlcvBar[]>> LoadReferenceBarsAsync(
        CompiledStrategy strategy,
        TimeFrame timeFrame,
        DateTime from,
        DateTime to,
        IOhlcvRepository repository,
        CancellationToken ct)
    {
        var result = new Dictionary<string, OhlcvBar[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in strategy.ReferenceSymbols)
        {
            result[symbol] = (await repository.GetBarsAsync(
                    symbol, timeFrame, from, to.AddDays(1), ct))
                .OrderBy(bar => bar.Timestamp)
                .ToArray();
        }
        return result;
    }

    private sealed class OpenPreviewPosition
    {
        public int EntryIndex { get; init; }
        public decimal EntryPrice { get; set; }
        public decimal StopPrice { get; set; }
        public decimal TargetPrice { get; init; }
        public decimal HighestPrice { get; set; }
        public decimal LowestPrice { get; set; }
        public decimal InitialRisk { get; init; }
        public decimal EntryAtr { get; init; }
        public int InitialQuantity { get; init; } = 100;
        public int CurrentQuantity { get; set; } = 100;
        public bool PartialProfitTaken { get; set; }
        public bool BreakevenApplied { get; set; }
        public bool TrailingActivated { get; set; }
        public decimal InvestedCapital { get; set; }
        public decimal RealizedPnl { get; set; }
        public decimal AllocationScale { get; init; } = 1m;
        public Dictionary<int, int> ScaleCounts { get; } = new();
    }

    private static DateTime DefaultFrom(TimeFrame timeFrame, DateTime to) =>
        PreviewTimeFramePolicy.Get(timeFrame).DefaultFrom(to);

    private static bool IsIntraday(TimeFrame timeFrame) => TimeFrameCatalog.IsIntraday(timeFrame);

    private static string DisplayMarketTime(DateTime value)
    {
        TimeZoneInfo marketTimeZone;
        try
        {
            marketTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            marketTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }

        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return $"{TimeZoneInfo.ConvertTimeFromUtc(utc, marketTimeZone):yyyy-MM-dd HH:mm} ET";
    }

    private static TimeSpan MaximumRange(TimeFrame timeFrame) =>
        PreviewTimeFramePolicy.Get(timeFrame).MaximumRange;

    private static TimeSpan WarmupRange(TimeFrame timeFrame) =>
        PreviewTimeFramePolicy.Get(timeFrame).WarmupRange;

    private static TimeSpan CoverageTolerance(TimeFrame timeFrame) =>
        PreviewTimeFramePolicy.Get(timeFrame).CoverageTolerance;

    private static string DisplayTimeFrame(TimeFrame timeFrame) =>
        TimeFrameCatalog.DisplayName(timeFrame);

    private static string DisplayRange(TimeSpan range) => range.TotalDays >= 365
        ? $"{Math.Floor(range.TotalDays / 365):N0}년"
        : $"{range.TotalDays:N0}일";
}

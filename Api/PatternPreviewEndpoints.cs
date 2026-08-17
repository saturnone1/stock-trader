using System.Text.Json;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
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
        CancellationToken ct)
    {
        var symbol = (request.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
            return Results.BadRequest(new { error = "미리보기 종목을 입력하세요." });

        if (request.Pattern is null)
            return Results.BadRequest(new { error = "미리보기 패턴 정의가 필요합니다." });

        var validationErrors = CustomPatternValidator.Validate(request.Pattern);
        if (validationErrors.Count > 0)
            return Results.BadRequest(new { error = validationErrors[0], errors = validationErrors });

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

        var detector = new RuleBasedDetector(indicators, request.Pattern);
        var referenceBars = await LoadReferenceBarsAsync(
            request.Pattern, request.TimeFrame, allBars[0].Timestamp, latest.Timestamp, ohlcvRepository, ct);
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

            if (position is not null && index > position.EntryIndex)
            {
                string? reason = null;
                decimal exitPrice = current.Close;

                position.HighestPrice = Math.Max(position.HighestPrice, current.High);
                var currentAtr = indicators.ATR(window, 14).LastOrDefault();
                var oldStop = position.StopPrice;
                if (!position.BreakevenApplied && currentAtr > 0
                    && current.Close >= position.EntryPrice + currentAtr * 1.5m)
                {
                    position.StopPrice = Math.Max(position.StopPrice, position.EntryPrice);
                    position.BreakevenApplied = true;
                }
                if (request.Pattern.TrailingAtr > 0
                    && current.Close >= position.EntryPrice + position.InitialRisk)
                {
                    position.TrailingActivated = true;
                    if (currentAtr > 0)
                        position.StopPrice = Math.Max(position.StopPrice,
                            position.HighestPrice - currentAtr * request.Pattern.TrailingAtr);
                }
                if (position.StopPrice > oldStop)
                {
                    markers.Add(new PatternPreviewMarker(
                        current.Timestamp, "STOP_MOVE", position.StopPrice,
                        Details: position.TrailingActivated ? "추적 손절가 상향" : "손절가를 매수가로 상향"));
                }

                if (request.Pattern.PartialProfitR > 0 && !position.PartialProfitTaken)
                {
                    var partialTarget = position.EntryPrice + position.InitialRisk * request.Pattern.PartialProfitR;
                    if (current.High >= partialTarget && position.CurrentQuantity >= 2)
                    {
                        var sold = position.CurrentQuantity / 2;
                        Realize(position, partialTarget, sold);
                        position.CurrentQuantity -= sold;
                        position.PartialProfitTaken = true;
                        position.StopPrice = Math.Max(position.StopPrice, position.EntryPrice);
                        markers.Add(new PatternPreviewMarker(
                            current.Timestamp, "PARTIAL_EXIT", partialTarget,
                            Details: $"보유 수량의 {sold * 100m / (sold + position.CurrentQuantity):F0}% 매도",
                            Reason: $"{request.Pattern.PartialProfitR:F1}R 부분 익절"));
                    }
                }

                if (current.Low <= position.StopPrice)
                {
                    reason = position.TrailingActivated || position.BreakevenApplied ? "추적 손절가 도달" : "손절가 도달";
                    exitPrice = position.StopPrice;
                }
                else if (current.High >= position.TargetPrice)
                {
                    reason = "목표가 도달";
                    exitPrice = position.TargetPrice;
                }
                else if (detector.ShouldExit(window))
                {
                    reason = "청산 규칙 충족";
                }
                else if (request.Pattern.MaxHoldingBars > 0 && index - position.EntryIndex >= request.Pattern.MaxHoldingBars)
                {
                    reason = "최대 보유기간";
                }

                if (reason is null && detector.HasScalingRules)
                {
                    var profitPercent = position.EntryPrice > 0
                        ? (current.Close - position.EntryPrice) / position.EntryPrice * 100m
                        : 0;
                    var scaling = detector.CheckScaling(window, profitPercent, position.ScaleCounts);
                    if (scaling is not null)
                    {
                        var amount = Math.Max(1, (int)Math.Round(position.InitialQuantity * scaling.Percent / 100m));
                        if (string.Equals(scaling.Direction, "SCALE_IN", StringComparison.OrdinalIgnoreCase))
                        {
                            var totalCost = position.EntryPrice * position.CurrentQuantity + current.Close * amount;
                            position.InvestedCapital += current.Close * amount;
                            position.CurrentQuantity += amount;
                            position.EntryPrice = totalCost / position.CurrentQuantity;
                            markers.Add(new PatternPreviewMarker(
                                current.Timestamp, "SCALE_IN", current.Close,
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
                                    current.Timestamp, "SCALE_OUT", current.Close,
                                    Details: $"최초 수량의 {scaling.Percent:F0}% 일부 매도"));
                            }
                        }
                    }
                }

                if (reason is not null)
                {
                    Complete(position, exitPrice);
                    markers.Add(new PatternPreviewMarker(current.Timestamp, "EXIT", exitPrice, Reason: reason));
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

            var entryIndex = index;
            var entryPrice = current.Close;
            var entryDate = current.Timestamp;
            if (string.Equals(request.Pattern.EntryMode, "NextOpen", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= allBars.Length)
                    continue;
                entryIndex = index + 1;
                entryPrice = allBars[entryIndex].Open;
                entryDate = allBars[entryIndex].Timestamp;
            }

            var stopDistance = Math.Max(0.0001m, signal.EntryPrice - signal.StopLossPrice);
            var targetMultiple = stopDistance > 0
                ? (signal.TargetPrice - signal.EntryPrice) / stopDistance
                : request.Pattern.AtrTargetMultiplier / request.Pattern.AtrStopMultiplier;
            var entryStop = entryPrice - stopDistance;
            var entryTarget = entryPrice + stopDistance * targetMultiple;
            position = new OpenPreviewPosition
            {
                EntryIndex = entryIndex,
                EntryPrice = entryPrice,
                StopPrice = entryStop,
                TargetPrice = entryTarget,
                HighestPrice = entryPrice,
                InitialRisk = stopDistance,
                InvestedCapital = entryPrice * 100,
                AllocationScale = signal.AllocationScale is > 0 and <= 1 ? signal.AllocationScale : 1m
            };
            markers.Add(new PatternPreviewMarker(
                entryDate,
                "ENTRY",
                entryPrice,
                entryStop,
                entryTarget,
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
                scaleInCount = visibleMarkers.Count(marker => marker.Type == "SCALE_IN"),
                partialExitCount = visibleMarkers.Count(marker => marker.Type is "SCALE_OUT" or "PARTIAL_EXIT"),
                stopMoveCount = visibleMarkers.Count(marker => marker.Type == "STOP_MOVE"),
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
        CustomPatternDefinition pattern,
        TimeFrame timeFrame,
        DateTime from,
        DateTime to,
        IOhlcvRepository repository,
        CancellationToken ct)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRules(IEnumerable<EntryRule> rules)
        {
            foreach (var rule in rules)
            {
                if (!string.IsNullOrWhiteSpace(rule.RefSymbol))
                    symbols.Add(rule.RefSymbol.Trim().ToUpperInvariant());
            }
        }

        try
        {
            AddRules(JsonSerializer.Deserialize<List<EntryRule>>(pattern.EntryRulesJson, options) ?? []);
            AddRules(JsonSerializer.Deserialize<List<EntryRule>>(pattern.ExitRulesJson, options) ?? []);
            foreach (var group in JsonSerializer.Deserialize<List<ConditionGroup>>(pattern.EntryGroupsJson, options) ?? [])
                AddRules(group.Rules);
            foreach (var group in JsonSerializer.Deserialize<List<ConditionGroup>>(pattern.ExitGroupsJson, options) ?? [])
                AddRules(group.Rules);
            foreach (var tier in JsonSerializer.Deserialize<List<WeightTier>>(pattern.WeightTiersJson, options) ?? [])
                AddRules(tier.Conditions);
            foreach (var scaling in JsonSerializer.Deserialize<List<ScalingRule>>(pattern.ScalingRulesJson, options) ?? [])
                AddRules(scaling.Conditions);
        }
        catch (JsonException)
        {
            return new Dictionary<string, OhlcvBar[]>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, OhlcvBar[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
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
        public decimal InitialRisk { get; init; }
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

    private static DateTime DefaultFrom(TimeFrame timeFrame, DateTime to) => timeFrame switch
    {
        TimeFrame.OneMinute => to.AddDays(-1),
        TimeFrame.FiveMinute => to.AddDays(-5),
        TimeFrame.FifteenMinute => to.AddDays(-20),
        TimeFrame.Weekly => to.AddYears(-5),
        _ => to.AddYears(-1)
    };

    private static bool IsIntraday(TimeFrame timeFrame) => timeFrame is
        TimeFrame.OneMinute or TimeFrame.FiveMinute or TimeFrame.FifteenMinute;

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

    private static TimeSpan MaximumRange(TimeFrame timeFrame) => timeFrame switch
    {
        TimeFrame.OneMinute => TimeSpan.FromDays(7),
        TimeFrame.FiveMinute => TimeSpan.FromDays(31),
        TimeFrame.FifteenMinute => TimeSpan.FromDays(120),
        TimeFrame.Weekly => TimeSpan.FromDays(365 * 15),
        _ => TimeSpan.FromDays(365 * 5)
    };

    private static TimeSpan WarmupRange(TimeFrame timeFrame) => timeFrame switch
    {
        TimeFrame.OneMinute => TimeSpan.FromDays(3),
        TimeFrame.FiveMinute => TimeSpan.FromDays(14),
        TimeFrame.FifteenMinute => TimeSpan.FromDays(45),
        TimeFrame.Weekly => TimeSpan.FromDays(365 * 5),
        _ => TimeSpan.FromDays(400)
    };

    private static TimeSpan CoverageTolerance(TimeFrame timeFrame) => timeFrame switch
    {
        TimeFrame.Weekly => TimeSpan.FromDays(14),
        TimeFrame.Daily => TimeSpan.FromDays(5),
        _ => TimeSpan.FromDays(4)
    };

    private static string DisplayTimeFrame(TimeFrame timeFrame) => timeFrame switch
    {
        TimeFrame.OneMinute => "1분봉",
        TimeFrame.FiveMinute => "5분봉",
        TimeFrame.FifteenMinute => "15분봉",
        TimeFrame.Weekly => "주봉",
        _ => "일봉"
    };

    private static string DisplayRange(TimeSpan range) => range.TotalDays >= 365
        ? $"{Math.Floor(range.TotalDays / 365):N0}년"
        : $"{range.TotalDays:N0}일";
}

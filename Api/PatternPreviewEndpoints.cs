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
    int Bars,
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

        var displayCount = Math.Clamp(request.Bars <= 0 ? 600 : request.Bars, 60, 600);
        var dataTo = (request.To ?? DateTime.UtcNow).Date.AddDays(1);
        var dataFrom = (request.From ?? DefaultFrom(request.TimeFrame, dataTo)).Date;
        if (dataFrom >= dataTo)
            return Results.BadRequest(new { error = "조회 시작일은 종료일보다 앞서야 합니다." });

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
        var displayStartIndex = requestedBars.Length > displayCount
            ? allBars.Length - requestedBars.Length + (requestedBars.Length - displayCount)
            : requestedStartIndex;
        var evaluationStartIndex = Math.Max(49, displayStartIndex);
        OpenPreviewPosition? position = null;

        if (requestedBars.Length > displayCount)
            warnings.Add($"선택 기간의 {requestedBars.Length:N0}개 봉 중 최근 {displayCount:N0}개를 표시합니다. 기간을 줄이거나 더 큰 봉 단위를 선택하세요.");

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

                if (current.Low <= position.StopPrice)
                {
                    reason = "손절가 도달";
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

                if (reason is not null)
                {
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

            position = new OpenPreviewPosition(
                entryIndex,
                entryPrice,
                signal.StopLossPrice,
                signal.TargetPrice);
            markers.Add(new PatternPreviewMarker(
                entryDate,
                "ENTRY",
                entryPrice,
                signal.StopLossPrice,
                signal.TargetPrice,
                signal.Details));
        }

        if (request.Pattern.TrailingAtr > 0 || request.Pattern.PartialProfitR > 0)
            warnings.Add("빠른 미리보기에서는 트레일링 ATR과 부분 익절의 체결 과정은 생략됩니다. 최종 성과는 백테스트에서 확인하세요.");

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
                openPosition = position is not null,
                from = displayStart.ToString("O"),
                to = allBars.Where(bar => bar.Timestamp < dataTo).Last().Timestamp.ToString("O"),
                requestedFrom = dataFrom.ToString("yyyy-MM-dd"),
                requestedTo = dataTo.AddDays(-1).ToString("yyyy-MM-dd"),
                requestedBarCount = requestedBars.Length,
                displayedBarCount = Math.Min(requestedBars.Length, displayCount)
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

    private sealed record OpenPreviewPosition(
        int EntryIndex,
        decimal EntryPrice,
        decimal StopPrice,
        decimal TargetPrice);

    private static DateTime DefaultFrom(TimeFrame timeFrame, DateTime to) => timeFrame switch
    {
        TimeFrame.OneMinute => to.AddDays(-1),
        TimeFrame.FiveMinute => to.AddDays(-5),
        TimeFrame.FifteenMinute => to.AddDays(-20),
        TimeFrame.Weekly => to.AddYears(-5),
        _ => to.AddYears(-1)
    };

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

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
    CustomPatternDefinition Pattern);

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

        var displayCount = Math.Clamp(request.Bars <= 0 ? 120 : request.Bars, 60, 240);
        var dataTo = DateTime.UtcNow.AddDays(1);
        var dataFrom = DateTime.UtcNow.AddYears(-3);
        var allBars = (await ohlcvRepository.GetBarsAsync(
                symbol, TimeFrame.Daily, dataFrom, dataTo, ct))
            .OrderBy(bar => bar.Timestamp)
            .GroupBy(bar => bar.Timestamp)
            .Select(group => group.Last())
            .TakeLast(displayCount + 260)
            .ToArray();

        if (allBars.Length < 50)
        {
            try
            {
                var dataFeed = await dataFeedFactory.GetServiceAsync(ct);
                var fetched = await dataFeed.GetHistoricalBarsAsync(
                    symbol, TimeFrame.Daily, dataFrom, dataTo, ct);
                if (fetched.Count > 0)
                    await ohlcvRepository.AddBarsAsync(fetched, ct);
            }
            catch (Exception)
            {
                return Results.Json(
                    new { error = $"{symbol} 일봉을 현재 데이터 제공자에서 가져오지 못했습니다." },
                    statusCode: StatusCodes.Status502BadGateway);
            }

            allBars = (await ohlcvRepository.GetBarsAsync(
                    symbol, TimeFrame.Daily, dataFrom, dataTo, ct))
                .OrderBy(bar => bar.Timestamp)
                .GroupBy(bar => bar.Timestamp)
                .Select(group => group.Last())
                .TakeLast(displayCount + 260)
                .ToArray();
        }

        if (allBars.Length < 50)
            return Results.NotFound(new { error = $"{symbol} 일봉을 찾을 수 없거나 패턴 평가에 필요한 데이터가 부족합니다." });

        var latest = allBars[^1];

        var detector = new RuleBasedDetector(indicators, request.Pattern);
        var referenceBars = await LoadReferenceBarsAsync(
            request.Pattern, allBars[0].Timestamp, latest.Timestamp, ohlcvRepository, ct);
        var spyBars = symbol == "SPY"
            ? allBars
            : (await ohlcvRepository.GetBarsAsync(
                    "SPY", TimeFrame.Daily, allBars[0].Timestamp.AddYears(-1), latest.Timestamp.AddDays(1), ct))
                .OrderBy(bar => bar.Timestamp)
                .ToArray();

        var markers = new List<PatternPreviewMarker>();
        var warnings = new List<string>();
        var displayStartIndex = Math.Max(0, allBars.Length - displayCount);
        var evaluationStartIndex = Math.Max(49, displayStartIndex);
        OpenPreviewPosition? position = null;

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

            if (position is not null)
                continue;

            var regime = BuildRegime(current.Timestamp, spyBars);
            var signal = await detector.DetectAsync(symbol, window, regime, ct);
            if (signal is null)
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

        var visibleBars = allBars.Skip(displayStartIndex).Select(bar => new
        {
            date = bar.Timestamp.ToString("yyyy-MM-dd"),
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            bar.Volume
        });
        var displayStart = allBars[displayStartIndex].Timestamp;
        var visibleMarkers = markers.Where(marker => marker.Date >= displayStart).ToList();

        return Results.Ok(new
        {
            symbol,
            bars = visibleBars,
            markers = visibleMarkers.Select(marker => new
            {
                date = marker.Date.ToString("yyyy-MM-dd"),
                type = marker.Type,
                marker.Price,
                marker.StopPrice,
                marker.TargetPrice,
                marker.Details,
                marker.Reason
            }),
            summary = new
            {
                entryCount = visibleMarkers.Count(marker => marker.Type == "ENTRY"),
                exitCount = visibleMarkers.Count(marker => marker.Type == "EXIT"),
                openPosition = position is not null,
                from = displayStart.ToString("yyyy-MM-dd"),
                to = allBars[^1].Timestamp.ToString("yyyy-MM-dd")
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
                    symbol, TimeFrame.Daily, from.AddYears(-1), to.AddDays(1), ct))
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
}

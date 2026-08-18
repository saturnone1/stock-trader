using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Services.Backtest;

namespace StockTrader.Services.LiveParameter;

public class LiveParameterService : ILiveParameterService
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly IOptions<PatternSettings> _patternSettings;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LiveParameterService> _logger;
    private readonly TimeProvider _timeProvider;

    public LiveParameterService(
        ISettingsRepository settingsRepo,
        IOptions<PatternSettings> patternSettings,
        IWebHostEnvironment env,
        ILogger<LiveParameterService> logger,
        TimeProvider timeProvider)
    {
        _settingsRepo = settingsRepo;
        _patternSettings = patternSettings;
        _env = env;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task ApplyToLiveAsync(
        PatternParameterOverrides overrides,
        List<PatternType> enabledPatterns,
        decimal riskPerTradePercent,
        decimal dailyLossLimitPercent,
        int maxTotalPositions,
        int maxPositionsPerSector,
        CancellationToken ct = default)
    {
        // 1. DB에 전체 오버라이드 저장 (청산 전략 포함)
        var settings = await _settingsRepo.GetAsync(ct);
        settings.LiveParameterOverridesJson = JsonSerializer.Serialize(overrides);
        settings.EnabledPatterns = enabledPatterns;
        settings.RiskPerTradePercent = riskPerTradePercent;
        settings.DailyLossLimitPercent = dailyLossLimitPercent;
        settings.MaxTotalPositions = maxTotalPositions;
        settings.MaxPositionsPerSector = maxPositionsPerSector;
        settings.LastModified = _timeProvider.GetUtcNow().UtcDateTime;
        await _settingsRepo.SaveAsync(settings, ct);

        _logger.LogInformation("Live parameter overrides saved to DB");

        // 2. 패턴 감지 파라미터 → appsettings.json 반영 (IOptionsSnapshot 핫리로드)
        var baseSettings = _patternSettings.Value;
        var merged = PatternOverrideMerger.Merge(baseSettings, overrides);
        merged.EnabledPatterns = enabledPatterns;
        await WriteToAppSettingsAsync(merged, riskPerTradePercent, dailyLossLimitPercent,
            maxTotalPositions, maxPositionsPerSector);

        _logger.LogInformation("Pattern settings written to appsettings.json (hot-reload active)");
    }

    public async Task<PatternParameterOverrides?> GetLiveOverridesAsync(CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetAsync(ct);
        if (string.IsNullOrEmpty(settings.LiveParameterOverridesJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PatternParameterOverrides>(settings.LiveParameterOverridesJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize LiveParameterOverridesJson");
            return null;
        }
    }

    private async Task WriteToAppSettingsAsync(
        PatternSettings merged,
        decimal riskPerTrade, decimal dailyLossLimit,
        int maxPositions, int maxPerSector)
    {
        var appSettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
        if (!File.Exists(appSettingsPath))
        {
            _logger.LogWarning("appsettings.json not found at {Path}", appSettingsPath);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(appSettingsPath);
            var doc = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (doc == null) return;

            // Patterns 섹션 업데이트
            var patternsNode = new JsonObject();

            // EnabledPatterns
            var enabledArray = new JsonArray();
            foreach (var p in merged.EnabledPatterns)
                enabledArray.Add(p.ToString());
            patternsNode["EnabledPatterns"] = enabledArray;

            // 각 패턴 Config를 JSON으로 직렬화
            patternsNode["GapUpPullback"] = SerializeConfig(merged.GapUpPullback);
            patternsNode["Breakout"] = SerializeConfig(merged.Breakout);
            patternsNode["VwapReversion"] = SerializeConfig(merged.VwapReversion);
            patternsNode["RsiMeanReversion"] = SerializeConfig(merged.RsiMeanReversion);
            patternsNode["TrendPullback"] = SerializeConfig(merged.TrendPullback);
            patternsNode["OpeningRangeBreakout"] = SerializeConfig(merged.OpeningRangeBreakout);
            patternsNode["VolumeSpikeContinuation"] = SerializeConfig(merged.VolumeSpikeContinuation);
            patternsNode["EarningsDrift"] = SerializeConfig(merged.EarningsDrift);
            patternsNode["IndexRegimeFilter"] = SerializeConfig(merged.IndexRegimeFilter);
            patternsNode["VolatilityExpansion"] = SerializeConfig(merged.VolatilityExpansion);
            patternsNode["MomentumReversal"] = SerializeConfig(merged.MomentumReversal);
            patternsNode["MultiTimeframeTrend"] = SerializeConfig(merged.MultiTimeframeTrend);
            patternsNode["MeanReversionChannel"] = SerializeConfig(merged.MeanReversionChannel);
            patternsNode["Rsi2Bollinger"] = SerializeConfig(merged.Rsi2Bollinger);
            patternsNode["CumulativeRsi2"] = SerializeConfig(merged.CumulativeRsi2);
            patternsNode["VolatilityBreakout"] = SerializeConfig(merged.VolatilityBreakout);
            patternsNode["Tqqq200Sma"] = SerializeConfig(merged.Tqqq200Sma);

            doc["Patterns"] = patternsNode;

            // Trading 섹션 업데이트 (리스크 파라미터)
            var trading = doc["Trading"];
            if (trading != null)
            {
                trading["RiskPerTradePercent"] = riskPerTrade;
                trading["DailyLossLimitPercent"] = dailyLossLimit;
                trading["MaxTotalPositions"] = maxPositions;
                trading["MaxPositionsPerSector"] = maxPerSector;
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            await File.WriteAllTextAsync(appSettingsPath, doc.ToJsonString(options));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write to appsettings.json");
        }
    }

    private static JsonNode SerializeConfig(object config)
    {
        var json = JsonSerializer.Serialize(config);
        return JsonNode.Parse(json)!;
    }
}

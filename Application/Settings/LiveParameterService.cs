using System.Text.Json;
using StockTrader.Models;

namespace StockTrader.Application.Settings;

public sealed record LiveParameterSnapshot(
    IReadOnlyList<PatternType> EnabledPatterns,
    PatternParameterOverrides? Overrides);

public sealed record LiveParameterApplyCommand(
    PatternParameterOverrides Overrides,
    IReadOnlyList<PatternType> EnabledPatterns,
    decimal RiskPerTradePercent,
    decimal DailyLossLimitPercent,
    int MaxTotalPositions,
    int MaxPositionsPerSector);

public sealed record LiveParameterApplyOutcome(
    ManagedSettings? Settings,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Settings is not null && Errors.Count == 0;
}

public interface ILiveParameterService
{
    Task<LiveParameterSnapshot> GetAsync(CancellationToken ct = default);
    Task<LiveParameterApplyOutcome> ApplyAsync(
        LiveParameterApplyCommand command,
        CancellationToken ct = default);
}

/// <summary>
/// 백테스트에서 검증한 내장 전략 파라미터를 실시간 실행 설정으로 승격합니다.
/// DB 스냅샷이 유일한 런타임 원천이며 애플리케이션 파일은 변경하지 않습니다.
/// </summary>
public sealed class LiveParameterService(
    ISettingsManagementStore store,
    TimeProvider timeProvider,
    ILogger<LiveParameterService> logger) : ILiveParameterService
{
    public async Task<LiveParameterSnapshot> GetAsync(CancellationToken ct = default)
    {
        var settings = await store.GetAsync(ct);
        return new(
            settings.EnabledPatterns
                .Where(PatternCatalog.IsOperationalBuiltIn)
                .Distinct()
                .ToArray(),
            DeserializeOverrides(settings.LiveParameterOverridesJson));
    }

    public async Task<LiveParameterApplyOutcome> ApplyAsync(
        LiveParameterApplyCommand command,
        CancellationToken ct = default)
    {
        var errors = Validate(command);
        if (errors.Count > 0)
            return new(null, errors);

        var current = await store.GetAsync(ct);
        var updated = current with
        {
            LiveParameterOverridesJson = JsonSerializer.Serialize(command.Overrides),
            EnabledPatterns = command.EnabledPatterns.Distinct().ToArray(),
            RiskPerTradePercent = command.RiskPerTradePercent,
            DailyLossLimitPercent = command.DailyLossLimitPercent,
            MaxTotalPositions = command.MaxTotalPositions,
            MaxPositionsPerSector = command.MaxPositionsPerSector,
            LastModified = timeProvider.GetUtcNow().UtcDateTime
        };
        await store.SaveAsync(updated, ct);
        logger.LogInformation("Validated live strategy parameters saved to the settings store");
        return new(updated, []);
    }

    private PatternParameterOverrides? DeserializeOverrides(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<PatternParameterOverrides>(json);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Stored live strategy parameters are invalid JSON; ignoring overrides");
            return null;
        }
    }

    private static List<string> Validate(LiveParameterApplyCommand command)
    {
        var errors = new List<string>();
        if (command.EnabledPatterns is null)
            errors.Add("실시간 감시 전략 목록이 필요합니다.");
        else if (command.EnabledPatterns.Any(pattern => !PatternCatalog.IsOperationalBuiltIn(pattern)))
            errors.Add("아직 실행할 수 없는 내장 전략은 실시간 감시에 적용할 수 없습니다.");
        if (command.RiskPerTradePercent is <= 0 or > 1)
            errors.Add("거래당 손실 허용률은 0보다 크고 1 이하여야 합니다.");
        if (command.DailyLossLimitPercent is <= 0 or > 1)
            errors.Add("일일 손실 한도는 0보다 크고 1 이하여야 합니다.");
        if (command.MaxTotalPositions <= 0)
            errors.Add("전체 최대 보유 종목 수는 1개 이상이어야 합니다.");
        if (command.MaxPositionsPerSector <= 0
            || command.MaxPositionsPerSector > command.MaxTotalPositions)
            errors.Add("업종별 최대 보유 수는 1 이상이며 전체 최대 보유 수 이하여야 합니다.");
        return errors;
    }
}

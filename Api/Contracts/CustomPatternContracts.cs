using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Api.Contracts;

/// <summary>클라이언트가 작성할 수 있는 저장 전략 필드. DB 키와 감사 시각은 포함하지 않는다.</summary>
public sealed class CustomPatternWriteRequest
{
    public int DocumentVersion { get; init; } = StrategyDocumentVersions.Current;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string EntryRulesJson { get; init; } = StrategyDocumentDefaults.EmptyListJson;
    public string EntryLogic { get; init; } = StrategyDocumentDefaults.AndLogic;
    public bool RequireBullRegime { get; init; }
    public decimal AtrStopMultiplier { get; init; } = StrategyDocumentDefaults.AtrStopMultiplier;
    public decimal AtrTargetMultiplier { get; init; } = StrategyDocumentDefaults.AtrTargetMultiplier;
    public int MaxHoldingBars { get; init; } = StrategyDocumentDefaults.MaxHoldingBars;
    public decimal TrailingAtr { get; init; }
    public decimal PartialProfitR { get; init; }
    public bool UseWeightTiers { get; init; }
    public string WeightTiersJson { get; init; } = StrategyDocumentDefaults.EmptyListJson;
    public decimal DefaultAllocationPercent { get; init; } = StrategyDocumentDefaults.DefaultAllocationPercent;
    public string ExitRulesJson { get; init; } = StrategyDocumentDefaults.EmptyListJson;
    public string ExitRulesLogic { get; init; } = StrategyDocumentDefaults.OrLogic;
    public string ExitGroupsJson { get; init; } = StrategyDocumentDefaults.EmptyListJson;
    public string ExitGroupsLogic { get; init; } = StrategyDocumentDefaults.OrLogic;
    public string ScalingRulesJson { get; init; } = StrategyDocumentDefaults.EmptyListJson;
    public string TimeFilterJson { get; init; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string CircuitBreakerJson { get; init; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string ReentryJson { get; init; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string PortfolioRulesJson { get; init; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string EntryGroupsJson { get; init; } = StrategyDocumentDefaults.EmptyListJson;
    public string EntryGroupsLogic { get; init; } = StrategyDocumentDefaults.AndLogic;
    public string DynamicExitJson { get; init; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string EntryMode { get; init; } = StrategyCatalog.CurrentCloseEntryMode;
    public TimeFrame TimeFrame { get; init; } = TimeFrame.Daily;
    public string SizingMode { get; init; } = StrategyCatalog.FixedRiskSizingMode;
    public bool IsActive { get; init; } = StrategyDocumentDefaults.IsActive;
    public bool EnableLiveTrading { get; init; }
}

/// <summary>저장 전략의 공개 읽기 계약.</summary>
public sealed record CustomPatternResponse(
    int Id,
    int DocumentVersion,
    string Name,
    string? Description,
    string EntryRulesJson,
    string EntryLogic,
    bool RequireBullRegime,
    decimal AtrStopMultiplier,
    decimal AtrTargetMultiplier,
    int MaxHoldingBars,
    decimal TrailingAtr,
    decimal PartialProfitR,
    bool UseWeightTiers,
    string WeightTiersJson,
    decimal DefaultAllocationPercent,
    string ExitRulesJson,
    string ExitRulesLogic,
    string ExitGroupsJson,
    string ExitGroupsLogic,
    string ScalingRulesJson,
    string TimeFilterJson,
    string CircuitBreakerJson,
    string ReentryJson,
    string PortfolioRulesJson,
    string EntryGroupsJson,
    string EntryGroupsLogic,
    string DynamicExitJson,
    string EntryMode,
    TimeFrame TimeFrame,
    string SizingMode,
    bool IsActive,
    bool EnableLiveTrading,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record BacktestApplyRequest(
    decimal? AtrStopMultiplier = null,
    decimal? AtrTargetMultiplier = null,
    int? MaxHoldingBars = null,
    decimal? TrailingAtr = null,
    decimal? PartialProfitR = null);

internal static class CustomPatternContractMapper
{
    public static CustomPatternDefinition ToDefinition(this CustomPatternWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var definition = new CustomPatternDefinition();
        ApplyTo(request, definition);
        return definition;
    }

    public static void ApplyTo(this CustomPatternWriteRequest request, CustomPatternDefinition target)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);
        target.DocumentVersion = request.DocumentVersion;
        target.Name = request.Name ?? string.Empty;
        target.Description = request.Description;
        target.EntryRulesJson = request.EntryRulesJson ?? StrategyDocumentDefaults.EmptyListJson;
        target.EntryLogic = request.EntryLogic ?? StrategyDocumentDefaults.AndLogic;
        target.RequireBullRegime = request.RequireBullRegime;
        target.AtrStopMultiplier = request.AtrStopMultiplier;
        target.AtrTargetMultiplier = request.AtrTargetMultiplier;
        target.MaxHoldingBars = request.MaxHoldingBars;
        target.TrailingAtr = request.TrailingAtr;
        target.PartialProfitR = request.PartialProfitR;
        target.UseWeightTiers = request.UseWeightTiers;
        target.WeightTiersJson = request.WeightTiersJson ?? StrategyDocumentDefaults.EmptyListJson;
        target.DefaultAllocationPercent = request.DefaultAllocationPercent;
        target.ExitRulesJson = request.ExitRulesJson ?? StrategyDocumentDefaults.EmptyListJson;
        target.ExitRulesLogic = request.ExitRulesLogic ?? StrategyDocumentDefaults.OrLogic;
        target.ExitGroupsJson = request.ExitGroupsJson ?? StrategyDocumentDefaults.EmptyListJson;
        target.ExitGroupsLogic = request.ExitGroupsLogic ?? StrategyDocumentDefaults.OrLogic;
        target.ScalingRulesJson = request.ScalingRulesJson ?? StrategyDocumentDefaults.EmptyListJson;
        target.TimeFilterJson = request.TimeFilterJson ?? StrategyDocumentDefaults.EmptyObjectJson;
        target.CircuitBreakerJson = request.CircuitBreakerJson ?? StrategyDocumentDefaults.EmptyObjectJson;
        target.ReentryJson = request.ReentryJson ?? StrategyDocumentDefaults.EmptyObjectJson;
        target.PortfolioRulesJson = request.PortfolioRulesJson ?? StrategyDocumentDefaults.EmptyObjectJson;
        target.EntryGroupsJson = request.EntryGroupsJson ?? StrategyDocumentDefaults.EmptyListJson;
        target.EntryGroupsLogic = request.EntryGroupsLogic ?? StrategyDocumentDefaults.AndLogic;
        target.DynamicExitJson = request.DynamicExitJson ?? StrategyDocumentDefaults.EmptyObjectJson;
        target.EntryMode = request.EntryMode ?? StrategyCatalog.CurrentCloseEntryMode;
        target.TimeFrame = request.TimeFrame;
        target.SizingMode = request.SizingMode ?? StrategyCatalog.FixedRiskSizingMode;
        target.IsActive = request.IsActive;
        target.EnableLiveTrading = request.EnableLiveTrading;
    }

    public static CustomPatternResponse ToResponse(this CustomPatternDefinition value) => new(
        value.Id,
        value.DocumentVersion,
        value.Name,
        value.Description,
        value.EntryRulesJson,
        value.EntryLogic,
        value.RequireBullRegime,
        value.AtrStopMultiplier,
        value.AtrTargetMultiplier,
        value.MaxHoldingBars,
        value.TrailingAtr,
        value.PartialProfitR,
        value.UseWeightTiers,
        value.WeightTiersJson,
        value.DefaultAllocationPercent,
        value.ExitRulesJson,
        value.ExitRulesLogic,
        value.ExitGroupsJson,
        value.ExitGroupsLogic,
        value.ScalingRulesJson,
        value.TimeFilterJson,
        value.CircuitBreakerJson,
        value.ReentryJson,
        value.PortfolioRulesJson,
        value.EntryGroupsJson,
        value.EntryGroupsLogic,
        value.DynamicExitJson,
        value.EntryMode,
        value.TimeFrame,
        value.SizingMode,
        value.IsActive,
        value.EnableLiveTrading,
        value.CreatedAt,
        value.UpdatedAt);
}

using StockTrader.Domain.Strategies;
using StockTrader.Application.Strategies;
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
    public static StrategyDocument ToStrategyDocument(this CustomPatternWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new StrategyDocument
        {
            DocumentVersion = request.DocumentVersion,
            Name = request.Name ?? string.Empty,
            Description = request.Description,
            EntryRulesJson = request.EntryRulesJson ?? StrategyDocumentDefaults.EmptyListJson,
            EntryLogic = request.EntryLogic ?? StrategyDocumentDefaults.AndLogic,
            RequireBullRegime = request.RequireBullRegime,
            AtrStopMultiplier = request.AtrStopMultiplier,
            AtrTargetMultiplier = request.AtrTargetMultiplier,
            MaxHoldingBars = request.MaxHoldingBars,
            TrailingAtr = request.TrailingAtr,
            PartialProfitR = request.PartialProfitR,
            UseWeightTiers = request.UseWeightTiers,
            WeightTiersJson = request.WeightTiersJson ?? StrategyDocumentDefaults.EmptyListJson,
            DefaultAllocationPercent = request.DefaultAllocationPercent,
            ExitRulesJson = request.ExitRulesJson ?? StrategyDocumentDefaults.EmptyListJson,
            ExitRulesLogic = request.ExitRulesLogic ?? StrategyDocumentDefaults.OrLogic,
            ExitGroupsJson = request.ExitGroupsJson ?? StrategyDocumentDefaults.EmptyListJson,
            ExitGroupsLogic = request.ExitGroupsLogic ?? StrategyDocumentDefaults.OrLogic,
            ScalingRulesJson = request.ScalingRulesJson ?? StrategyDocumentDefaults.EmptyListJson,
            TimeFilterJson = request.TimeFilterJson ?? StrategyDocumentDefaults.EmptyObjectJson,
            CircuitBreakerJson = request.CircuitBreakerJson ?? StrategyDocumentDefaults.EmptyObjectJson,
            ReentryJson = request.ReentryJson ?? StrategyDocumentDefaults.EmptyObjectJson,
            PortfolioRulesJson = request.PortfolioRulesJson ?? StrategyDocumentDefaults.EmptyObjectJson,
            EntryGroupsJson = request.EntryGroupsJson ?? StrategyDocumentDefaults.EmptyListJson,
            EntryGroupsLogic = request.EntryGroupsLogic ?? StrategyDocumentDefaults.AndLogic,
            DynamicExitJson = request.DynamicExitJson ?? StrategyDocumentDefaults.EmptyObjectJson,
            EntryMode = request.EntryMode ?? StrategyCatalog.CurrentCloseEntryMode,
            TimeFrame = request.TimeFrame,
            SizingMode = request.SizingMode ?? StrategyCatalog.FixedRiskSizingMode,
            IsActive = request.IsActive,
            EnableLiveTrading = request.EnableLiveTrading,
        };
    }

    public static CustomPatternResponse ToResponse(this StoredStrategy value) => new(
        value.Id,
        value.Document.DocumentVersion,
        value.Document.Name,
        value.Document.Description,
        value.Document.EntryRulesJson,
        value.Document.EntryLogic,
        value.Document.RequireBullRegime,
        value.Document.AtrStopMultiplier,
        value.Document.AtrTargetMultiplier,
        value.Document.MaxHoldingBars,
        value.Document.TrailingAtr,
        value.Document.PartialProfitR,
        value.Document.UseWeightTiers,
        value.Document.WeightTiersJson,
        value.Document.DefaultAllocationPercent,
        value.Document.ExitRulesJson,
        value.Document.ExitRulesLogic,
        value.Document.ExitGroupsJson,
        value.Document.ExitGroupsLogic,
        value.Document.ScalingRulesJson,
        value.Document.TimeFilterJson,
        value.Document.CircuitBreakerJson,
        value.Document.ReentryJson,
        value.Document.PortfolioRulesJson,
        value.Document.EntryGroupsJson,
        value.Document.EntryGroupsLogic,
        value.Document.DynamicExitJson,
        value.Document.EntryMode,
        value.Document.TimeFrame,
        value.Document.SizingMode,
        value.Document.IsActive,
        value.Document.EnableLiveTrading,
        value.CreatedAt,
        value.UpdatedAt);
}

using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.Strategies;

/// <summary>
/// 저장 방식과 무관하게 미리보기·백테스트·최적화·실시간 실행이 공유하는 전략 문서다.
/// DB 전용 비교 키와 감사 시각은 포함하지 않으며, 저장 전략을 가리킬 때만 선택적 ID를 사용한다.
/// </summary>
public sealed class StrategyDocument
{
    public int? StoredStrategyId { get; set; }
    public int DocumentVersion { get; set; } = StrategyDocumentVersions.Current;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string EntryRulesJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;
    public string EntryLogic { get; set; } = StrategyDocumentDefaults.AndLogic;
    public bool RequireBullRegime { get; set; }
    public decimal AtrStopMultiplier { get; set; } = StrategyDocumentDefaults.AtrStopMultiplier;
    public decimal AtrTargetMultiplier { get; set; } = StrategyDocumentDefaults.AtrTargetMultiplier;
    public int MaxHoldingBars { get; set; } = StrategyDocumentDefaults.MaxHoldingBars;
    public decimal TrailingAtr { get; set; }
    public decimal PartialProfitR { get; set; }
    public bool UseWeightTiers { get; set; }
    public string WeightTiersJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;
    public decimal DefaultAllocationPercent { get; set; } = StrategyDocumentDefaults.DefaultAllocationPercent;
    public string ExitRulesJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;
    public string ExitRulesLogic { get; set; } = StrategyDocumentDefaults.OrLogic;
    public string ExitGroupsJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;
    public string ExitGroupsLogic { get; set; } = StrategyDocumentDefaults.OrLogic;
    public string ScalingRulesJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;
    public string TimeFilterJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string CircuitBreakerJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string ReentryJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string PortfolioRulesJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string EntryGroupsJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;
    public string EntryGroupsLogic { get; set; } = StrategyDocumentDefaults.AndLogic;
    public string DynamicExitJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string EntryMode { get; set; } = StrategyCatalog.CurrentCloseEntryMode;
    public TimeFrame TimeFrame { get; set; } = TimeFrame.Daily;
    public string SizingMode { get; set; } = StrategyCatalog.FixedRiskSizingMode;
    public bool IsActive { get; set; } = StrategyDocumentDefaults.IsActive;
    public bool EnableLiveTrading { get; set; }
}

/// <summary>실행 문서와 현재 EF 저장 엔티티 사이의 단일 변환 경계.</summary>
public static class StrategyDocumentMapper
{
    public static StrategyDocument ToStrategyDocument(this CustomPatternDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new StrategyDocument
        {
            StoredStrategyId = value.Id > 0 ? value.Id : null,
            DocumentVersion = value.DocumentVersion,
            Name = value.Name,
            Description = value.Description,
            EntryRulesJson = value.EntryRulesJson,
            EntryLogic = value.EntryLogic,
            RequireBullRegime = value.RequireBullRegime,
            AtrStopMultiplier = value.AtrStopMultiplier,
            AtrTargetMultiplier = value.AtrTargetMultiplier,
            MaxHoldingBars = value.MaxHoldingBars,
            TrailingAtr = value.TrailingAtr,
            PartialProfitR = value.PartialProfitR,
            UseWeightTiers = value.UseWeightTiers,
            WeightTiersJson = value.WeightTiersJson,
            DefaultAllocationPercent = value.DefaultAllocationPercent,
            ExitRulesJson = value.ExitRulesJson,
            ExitRulesLogic = value.ExitRulesLogic,
            ExitGroupsJson = value.ExitGroupsJson,
            ExitGroupsLogic = value.ExitGroupsLogic,
            ScalingRulesJson = value.ScalingRulesJson,
            TimeFilterJson = value.TimeFilterJson,
            CircuitBreakerJson = value.CircuitBreakerJson,
            ReentryJson = value.ReentryJson,
            PortfolioRulesJson = value.PortfolioRulesJson,
            EntryGroupsJson = value.EntryGroupsJson,
            EntryGroupsLogic = value.EntryGroupsLogic,
            DynamicExitJson = value.DynamicExitJson,
            EntryMode = value.EntryMode,
            TimeFrame = value.TimeFrame,
            SizingMode = value.SizingMode,
            IsActive = value.IsActive,
            EnableLiveTrading = value.EnableLiveTrading,
        };
    }

    public static CustomPatternDefinition ToStoredDefinition(this StrategyDocument value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var target = new CustomPatternDefinition();
        value.ApplyToStoredDefinition(target);
        return target;
    }

    public static void ApplyToStoredDefinition(this StrategyDocument value, CustomPatternDefinition target)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(target);
        target.DocumentVersion = value.DocumentVersion;
        target.Name = value.Name;
        target.Description = value.Description;
        target.EntryRulesJson = value.EntryRulesJson;
        target.EntryLogic = value.EntryLogic;
        target.RequireBullRegime = value.RequireBullRegime;
        target.AtrStopMultiplier = value.AtrStopMultiplier;
        target.AtrTargetMultiplier = value.AtrTargetMultiplier;
        target.MaxHoldingBars = value.MaxHoldingBars;
        target.TrailingAtr = value.TrailingAtr;
        target.PartialProfitR = value.PartialProfitR;
        target.UseWeightTiers = value.UseWeightTiers;
        target.WeightTiersJson = value.WeightTiersJson;
        target.DefaultAllocationPercent = value.DefaultAllocationPercent;
        target.ExitRulesJson = value.ExitRulesJson;
        target.ExitRulesLogic = value.ExitRulesLogic;
        target.ExitGroupsJson = value.ExitGroupsJson;
        target.ExitGroupsLogic = value.ExitGroupsLogic;
        target.ScalingRulesJson = value.ScalingRulesJson;
        target.TimeFilterJson = value.TimeFilterJson;
        target.CircuitBreakerJson = value.CircuitBreakerJson;
        target.ReentryJson = value.ReentryJson;
        target.PortfolioRulesJson = value.PortfolioRulesJson;
        target.EntryGroupsJson = value.EntryGroupsJson;
        target.EntryGroupsLogic = value.EntryGroupsLogic;
        target.DynamicExitJson = value.DynamicExitJson;
        target.EntryMode = value.EntryMode;
        target.TimeFrame = value.TimeFrame;
        target.SizingMode = value.SizingMode;
        target.IsActive = value.IsActive;
        target.EnableLiveTrading = value.EnableLiveTrading;
    }
}

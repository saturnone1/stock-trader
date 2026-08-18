using StockTrader.Application.StrategyPreview;
using StockTrader.Application.Strategies;
using StockTrader.Domain.Backtesting;
using StockTrader.Domain.MarketData;
using StockTrader.Domain.Optimization;
using StockTrader.Domain.Strategies;
using StockTrader.Models.Enums;

namespace StockTrader.Api.Contracts;

public sealed record IndicatorParameterMetadataResponse(
    string Key,
    string DisplayName,
    decimal DefaultValue,
    decimal Step,
    bool MustBePositive);

public sealed record IndicatorMetadataResponse(
    string Code,
    string DisplayName,
    string Category,
    string DefaultOperator,
    decimal DefaultThreshold,
    string? ValueGuide,
    IReadOnlyList<IndicatorParameterMetadataResponse> Parameters);

public sealed record PreviewTimeFrameMetadataResponse(
    int DefaultLookbackDays,
    int MaximumRangeDays,
    IReadOnlyList<int> SuggestedRangeDays);

public sealed record TimeFrameMetadataResponse(
    TimeFrame Value,
    string DisplayName,
    bool IsIntraday,
    decimal AnnualizationPeriods,
    PreviewTimeFrameMetadataResponse Preview);

public sealed record DataProviderMetadataResponse(
    DataSource Value,
    string DisplayName,
    string Market,
    IReadOnlyList<TimeFrame> SupportedTimeFrames,
    IReadOnlyDictionary<TimeFrame, int> MaximumLookbackDays);

public sealed record StrategyOptionMetadataResponse(string Code, string DisplayName);
public sealed record SlippageModelMetadataResponse(
    SlippageModel Value,
    string DisplayName,
    string Description,
    bool IsDefault);
public sealed record OptimizationRankMetadataResponse(
    string Code,
    string DisplayName,
    bool IsDefault);
public sealed record ExitMethodMetadataResponse(
    string Code,
    string DisplayName,
    IReadOnlyList<IndicatorParameterMetadataResponse> Parameters);
public sealed record LiveStrategyConstraintsMetadataResponse(
    IReadOnlyList<TimeFrame> SupportedTimeFrames,
    IReadOnlyList<string> SupportedEntryModes,
    bool SupportsPartialExit,
    bool SupportsScaling);

public sealed record StrategyBuilderMetadataResponse(
    int SchemaVersion,
    int DocumentVersion,
    IReadOnlyList<IndicatorMetadataResponse> Indicators,
    IReadOnlyList<TimeFrameMetadataResponse> TimeFrames,
    IReadOnlyList<DataProviderMetadataResponse> DataProviders,
    IReadOnlyList<string> RuleOperators,
    IReadOnlyList<StrategyOptionMetadataResponse> EntryModes,
    IReadOnlyList<StrategyOptionMetadataResponse> SizingModes,
    IReadOnlyList<StrategyOptionMetadataResponse> LogicModes,
    IReadOnlyList<StrategyOptionMetadataResponse> ScalingDirections,
    IReadOnlyList<ExitMethodMetadataResponse> StopMethods,
    IReadOnlyList<ExitMethodMetadataResponse> TargetMethods,
    IReadOnlyList<SlippageModelMetadataResponse> SlippageModels,
    IReadOnlyList<OptimizationRankMetadataResponse> OptimizationRankings,
    LiveStrategyConstraintsMetadataResponse LiveStrategyConstraints)
{
    public static StrategyBuilderMetadataResponse Create() => new(
        SchemaVersion: 4,
        DocumentVersion: StrategyDocumentVersions.Current,
        Indicators: IndicatorCatalog.All.Select(item => new IndicatorMetadataResponse(
            item.Code,
            item.DisplayName,
            item.Category,
            item.DefaultOperator,
            item.DefaultThreshold,
            item.ValueGuide,
            item.Parameters.Select(parameter => new IndicatorParameterMetadataResponse(
                parameter.Key,
                parameter.DisplayName,
                parameter.DefaultValue,
                parameter.Step,
                parameter.MustBePositive)).ToArray())).ToArray(),
        TimeFrames: TimeFrameCatalog.All.Select(item =>
        {
            var preview = PreviewTimeFramePolicy.Get(item.Value);
            return new TimeFrameMetadataResponse(
                item.Value,
                item.DisplayName,
                item.IsIntraday,
                item.AnnualizationPeriods,
                new PreviewTimeFrameMetadataResponse(
                    preview.DefaultLookbackDays,
                    (int)preview.MaximumRange.TotalDays,
                    preview.SuggestedRangeDays));
        }).ToArray(),
        DataProviders: DataProviderCatalog.Implemented.Select(item => new DataProviderMetadataResponse(
            item.Value,
            item.DisplayName,
            item.Market,
            item.SupportedTimeFrames,
            item.MaximumLookbackDays)).ToArray(),
        RuleOperators: RuleOperatorCatalog.All,
        EntryModes: StrategyCatalog.EntryModes.Select(ToResponse).ToArray(),
        SizingModes: StrategyCatalog.SizingModes.Select(ToResponse).ToArray(),
        LogicModes: StrategyCatalog.LogicModes.Select(ToResponse).ToArray(),
        ScalingDirections: StrategyCatalog.ScalingDirections.Select(ToResponse).ToArray(),
        StopMethods: StrategyCatalog.StopMethods.Select(ToResponse).ToArray(),
        TargetMethods: StrategyCatalog.TargetMethods.Select(ToResponse).ToArray(),
        SlippageModels: BacktestExecutionCatalog.SlippageModels.Select(item =>
            new SlippageModelMetadataResponse(
                item.Value,
                item.DisplayName,
                item.Description,
                item.IsDefault)).ToArray(),
        OptimizationRankings: OptimizationRankingCatalog.All.Select(item =>
            new OptimizationRankMetadataResponse(
                item.Code,
                item.DisplayName,
                item.IsDefault)).ToArray(),
        LiveStrategyConstraints: new(
            LiveStrategyCompatibilityPolicy.SupportedTimeFrames,
            LiveStrategyCompatibilityPolicy.SupportedEntryModes,
            LiveStrategyCompatibilityPolicy.SupportsPartialExit,
            LiveStrategyCompatibilityPolicy.SupportsScaling));

    private static StrategyOptionMetadataResponse ToResponse(StrategyOptionDescriptor item) =>
        new(item.Code, item.DisplayName);

    private static ExitMethodMetadataResponse ToResponse(ExitMethodDescriptor item) =>
        new(item.Code, item.DisplayName, item.Parameters.Select(parameter => new IndicatorParameterMetadataResponse(
            parameter.Key, parameter.DisplayName, parameter.DefaultValue, parameter.Step, parameter.MustBePositive)).ToArray());
}

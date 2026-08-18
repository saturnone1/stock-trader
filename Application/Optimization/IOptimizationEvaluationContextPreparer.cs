using StockTrader.Models.Enums;

namespace StockTrader.Application.Optimization;

public enum OptimizationPreparationFailure
{
    RegimeDataUnavailable,
    NoUsableSymbolData
}

public sealed record OptimizationPreparationResult(
    OptimizationEvaluationContext? Context,
    OptimizationPreparationFailure? Failure,
    string Message)
{
    public bool IsSuccess => Context is not null && Failure is null;

    public static OptimizationPreparationResult Success(
        OptimizationEvaluationContext context) => new(context, null, string.Empty);

    public static OptimizationPreparationResult Failed(
        OptimizationPreparationFailure failure,
        string message) => new(null, failure, message);
}

/// <summary>최적화 실행에 필요한 피드·레짐·타임프레임별 데이터를 한 번 준비합니다.</summary>
public interface IOptimizationEvaluationContextPreparer
{
    Task<OptimizationPreparationResult> PrepareAsync(
        OptimizeRequest request,
        CancellationToken ct);
}

/// <summary>외부 I/O와 무관한 최적화 데이터 준비 목록 정책입니다.</summary>
public static class OptimizationDataPreparationPolicy
{
    public static IReadOnlyList<TimeFrame> ResolveTimeFrames(OptimizeRequest request) =>
        request.OptimizeParams.TimeFrameOptions is { Count: > 0 }
            ? request.OptimizeParams.TimeFrameOptions
                .Select(value => (TimeFrame)value)
                .Distinct()
                .ToArray()
            : [request.TimeFrame];

    public static IReadOnlyList<string> ResolveSymbols(
        OptimizeRequest request,
        IEnumerable<string> referenceSymbols) =>
        request.Symbols
            .Concat(referenceSymbols)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

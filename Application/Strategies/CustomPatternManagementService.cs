using StockTrader.Models;
using StockTrader.Domain.Strategies;

namespace StockTrader.Application.Strategies;

public enum CustomPatternOperationKind
{
    Success,
    Invalid,
    Conflict,
    NotFound
}

public sealed record CustomPatternOperationResult(
    CustomPatternOperationKind Kind,
    CustomPatternDefinition? Definition = null,
    string? Error = null,
    IReadOnlyList<string>? Errors = null)
{
    public static CustomPatternOperationResult Success(CustomPatternDefinition definition) =>
        new(CustomPatternOperationKind.Success, definition);

    public static CustomPatternOperationResult Invalid(IReadOnlyList<string> errors) =>
        new(CustomPatternOperationKind.Invalid, Error: errors.FirstOrDefault(), Errors: errors);

    public static CustomPatternOperationResult Conflict(string error) =>
        new(CustomPatternOperationKind.Conflict, Error: error);

    public static CustomPatternOperationResult NotFound(string error) =>
        new(CustomPatternOperationKind.NotFound, Error: error);
}

public sealed record BacktestStrategyParameterUpdate(
    decimal? AtrStopMultiplier,
    decimal? AtrTargetMultiplier,
    int? MaxHoldingBars,
    decimal? TrailingAtr,
    decimal? PartialProfitR);

/// <summary>
/// 저장 전략의 생성·수정·삭제와 백테스트 결과 반영을 한 경계에서 검증한다.
/// HTTP 상태와 EF 추적 방식은 소유하지 않는다.
/// </summary>
public sealed class CustomPatternManagementService
{
    private const string DuplicateNameError = "같은 이름의 전략이 이미 있습니다. 다른 이름을 사용하세요.";
    private readonly ICustomPatternStore _store;
    private readonly TimeProvider _clock;

    public CustomPatternManagementService(ICustomPatternStore store, TimeProvider clock)
    {
        _store = store;
        _clock = clock;
    }

    public Task<IReadOnlyList<CustomPatternDefinition>> ListAsync(CancellationToken ct = default) =>
        _store.ListAsync(ct);

    public Task<CustomPatternDefinition?> FindAsync(int id, CancellationToken ct = default) =>
        _store.FindAsync(id, ct);

    public Task<CustomPatternDefinition?> FindByNameAsync(string name, CancellationToken ct = default) =>
        _store.FindByNameAsync(StoredStrategyName.Normalize(name), ct);

    public async Task<CustomPatternOperationResult> CreateAsync(
        CustomPatternDefinition input,
        CancellationToken ct = default)
    {
        var validation = Validate(input);
        if (validation is not null) return validation;

        input.Name = input.Name.Trim();
        input.NormalizedName = StoredStrategyName.Normalize(input.Name);
        if (await _store.NameExistsAsync(input.NormalizedName, ct: ct))
            return CustomPatternOperationResult.Conflict(DuplicateNameError);

        input.Id = 0;
        StrategyDocumentVersionPolicy.StampCurrent(input);
        input.CreatedAt = _clock.GetUtcNow().UtcDateTime;
        input.UpdatedAt = input.CreatedAt;
        if (await _store.AddAsync(input, ct) == CustomPatternWriteResult.NameConflict)
            return CustomPatternOperationResult.Conflict(DuplicateNameError);

        return CustomPatternOperationResult.Success(input);
    }

    public async Task<CustomPatternOperationResult> UpdateAsync(
        int id,
        CustomPatternDefinition input,
        CancellationToken ct = default)
    {
        var validation = Validate(input);
        if (validation is not null) return validation;

        var existing = await _store.FindAsync(id, ct);
        if (existing is null)
            return CustomPatternOperationResult.NotFound("수정할 전략을 찾을 수 없습니다.");

        input.Name = input.Name.Trim();
        input.NormalizedName = StoredStrategyName.Normalize(input.Name);
        if (await _store.NameExistsAsync(input.NormalizedName, id, ct))
            return CustomPatternOperationResult.Conflict(DuplicateNameError);

        input.Id = id;
        input.CreatedAt = existing.CreatedAt;
        input.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        StrategyDocumentVersionPolicy.StampCurrent(input);
        if (await _store.UpdateAsync(input, ct) == CustomPatternWriteResult.NameConflict)
            return CustomPatternOperationResult.Conflict(DuplicateNameError);

        return CustomPatternOperationResult.Success(input);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken ct = default) =>
        _store.DeleteAsync(id, ct);

    public async Task<CustomPatternOperationResult> ApplyBacktestAsync(
        int id,
        BacktestStrategyParameterUpdate update,
        CancellationToken ct = default)
    {
        var pattern = await _store.FindAsync(id, ct);
        if (pattern is null)
            return CustomPatternOperationResult.NotFound("반영할 전략을 찾을 수 없습니다.");

        if (update.AtrStopMultiplier.HasValue) pattern.AtrStopMultiplier = update.AtrStopMultiplier.Value;
        if (update.AtrTargetMultiplier.HasValue) pattern.AtrTargetMultiplier = update.AtrTargetMultiplier.Value;
        if (update.MaxHoldingBars.HasValue) pattern.MaxHoldingBars = update.MaxHoldingBars.Value;
        if (update.TrailingAtr.HasValue) pattern.TrailingAtr = update.TrailingAtr.Value;
        if (update.PartialProfitR.HasValue) pattern.PartialProfitR = update.PartialProfitR.Value;

        var validation = Validate(pattern);
        if (validation is not null) return validation;

        StrategyDocumentVersionPolicy.StampCurrent(pattern);
        pattern.NormalizedName = StoredStrategyName.Normalize(pattern.Name);
        pattern.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        if (await _store.UpdateAsync(pattern, ct) == CustomPatternWriteResult.NameConflict)
            return CustomPatternOperationResult.Conflict(DuplicateNameError);

        return CustomPatternOperationResult.Success(pattern);
    }

    private static CustomPatternOperationResult? Validate(CustomPatternDefinition input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = StrategyCompiler.Compile(input.ToStrategyDocument()).Errors;
        return errors.Count == 0 ? null : CustomPatternOperationResult.Invalid(errors);
    }
}

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
    StoredStrategy? Strategy = null,
    string? Error = null,
    IReadOnlyList<string>? Errors = null)
{
    public static CustomPatternOperationResult Success(StoredStrategy strategy) =>
        new(CustomPatternOperationKind.Success, strategy);

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

    public Task<IReadOnlyList<StoredStrategy>> ListAsync(CancellationToken ct = default) =>
        _store.ListAsync(ct);

    public Task<StoredStrategy?> FindAsync(int id, CancellationToken ct = default) =>
        _store.FindAsync(id, ct);

    public Task<StoredStrategy?> FindByNameAsync(string name, CancellationToken ct = default) =>
        _store.FindByNameAsync(StoredStrategyName.Normalize(name), ct);

    public async Task<CustomPatternOperationResult> CreateAsync(
        StrategyDocument input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var document = input.Copy();
        document.Name = document.Name.Trim();
        document.StoredStrategyId = null;
        var validation = Validate(document);
        if (validation is not null) return validation;

        var normalizedName = StoredStrategyName.Normalize(document.Name);
        if (await _store.NameExistsAsync(normalizedName, ct: ct))
            return CustomPatternOperationResult.Conflict(DuplicateNameError);

        StrategyDocumentVersionPolicy.StampCurrent(document);
        var now = _clock.GetUtcNow().UtcDateTime;
        var write = await _store.AddAsync(new StoredStrategy(0, document, now, now), ct);
        if (write.Result == CustomPatternWriteResult.NameConflict)
            return CustomPatternOperationResult.Conflict(DuplicateNameError);

        return CustomPatternOperationResult.Success(write.Strategy!);
    }

    public async Task<CustomPatternOperationResult> UpdateAsync(
        int id,
        StrategyDocument input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var document = input.Copy();
        document.Name = document.Name.Trim();
        document.StoredStrategyId = id;
        var validation = Validate(document);
        if (validation is not null) return validation;

        var existing = await _store.FindAsync(id, ct);
        if (existing is null)
            return CustomPatternOperationResult.NotFound("수정할 전략을 찾을 수 없습니다.");

        var normalizedName = StoredStrategyName.Normalize(document.Name);
        if (await _store.NameExistsAsync(normalizedName, id, ct))
            return CustomPatternOperationResult.Conflict(DuplicateNameError);

        StrategyDocumentVersionPolicy.StampCurrent(document);
        var candidate = new StoredStrategy(
            id, document, existing.CreatedAt, _clock.GetUtcNow().UtcDateTime);
        var write = await _store.UpdateAsync(candidate, ct);
        if (write.Result == CustomPatternWriteResult.NameConflict)
            return CustomPatternOperationResult.Conflict(DuplicateNameError);
        if (write.Result == CustomPatternWriteResult.NotFound)
            return CustomPatternOperationResult.NotFound("수정할 전략을 찾을 수 없습니다.");

        return CustomPatternOperationResult.Success(write.Strategy!);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken ct = default) =>
        _store.DeleteAsync(id, ct);

    public async Task<CustomPatternOperationResult> ApplyBacktestAsync(
        int id,
        BacktestStrategyParameterUpdate update,
        CancellationToken ct = default)
    {
        var stored = await _store.FindAsync(id, ct);
        if (stored is null)
            return CustomPatternOperationResult.NotFound("반영할 전략을 찾을 수 없습니다.");

        var document = stored.Document.Copy();
        if (update.AtrStopMultiplier.HasValue) document.AtrStopMultiplier = update.AtrStopMultiplier.Value;
        if (update.AtrTargetMultiplier.HasValue) document.AtrTargetMultiplier = update.AtrTargetMultiplier.Value;
        if (update.MaxHoldingBars.HasValue) document.MaxHoldingBars = update.MaxHoldingBars.Value;
        if (update.TrailingAtr.HasValue) document.TrailingAtr = update.TrailingAtr.Value;
        if (update.PartialProfitR.HasValue) document.PartialProfitR = update.PartialProfitR.Value;

        var validation = Validate(document);
        if (validation is not null) return validation;

        StrategyDocumentVersionPolicy.StampCurrent(document);
        var candidate = stored with
        {
            Document = document,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime
        };
        var write = await _store.UpdateAsync(candidate, ct);
        if (write.Result == CustomPatternWriteResult.NameConflict)
            return CustomPatternOperationResult.Conflict(DuplicateNameError);
        if (write.Result == CustomPatternWriteResult.NotFound)
            return CustomPatternOperationResult.NotFound("반영할 전략을 찾을 수 없습니다.");

        return CustomPatternOperationResult.Success(write.Strategy!);
    }

    private static CustomPatternOperationResult? Validate(StrategyDocument input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = StrategyCompiler.Compile(input).Errors;
        return errors.Count == 0 ? null : CustomPatternOperationResult.Invalid(errors);
    }
}

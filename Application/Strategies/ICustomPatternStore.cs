namespace StockTrader.Application.Strategies;

public enum CustomPatternWriteResult
{
    Saved,
    NameConflict,
    NotFound
}

/// <summary>전략 의미와 서버 소유 저장 메타데이터를 분리한 애플리케이션 읽기 모델.</summary>
public sealed record StoredStrategy(
    int Id,
    StrategyDocument Document,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CustomPatternStoreWriteOutcome(
    CustomPatternWriteResult Result,
    StoredStrategy? Strategy = null)
{
    public static CustomPatternStoreWriteOutcome Saved(StoredStrategy strategy) =>
        new(CustomPatternWriteResult.Saved, strategy);

    public static CustomPatternStoreWriteOutcome NameConflict() =>
        new(CustomPatternWriteResult.NameConflict);

    public static CustomPatternStoreWriteOutcome NotFound() =>
        new(CustomPatternWriteResult.NotFound);
}

/// <summary>저장 전략 관리 유스케이스가 요구하는 목적별 영속 포트.</summary>
public interface ICustomPatternStore
{
    Task<IReadOnlyList<StoredStrategy>> ListAsync(CancellationToken ct = default);
    Task<StoredStrategy?> FindAsync(int id, CancellationToken ct = default);
    Task<StoredStrategy?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string normalizedName, int? excludingId = null, CancellationToken ct = default);
    Task<CustomPatternStoreWriteOutcome> AddAsync(StoredStrategy strategy, CancellationToken ct = default);
    Task<CustomPatternStoreWriteOutcome> UpdateAsync(StoredStrategy strategy, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

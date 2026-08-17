using StockTrader.Models;

namespace StockTrader.Application.Strategies;

public enum CustomPatternWriteResult
{
    Saved,
    NameConflict
}

/// <summary>저장 전략 관리 유스케이스가 요구하는 목적별 영속 포트.</summary>
public interface ICustomPatternStore
{
    Task<IReadOnlyList<CustomPatternDefinition>> ListAsync(CancellationToken ct = default);
    Task<CustomPatternDefinition?> FindAsync(int id, CancellationToken ct = default);
    Task<CustomPatternDefinition?> FindByNameAsync(string name, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string normalizedName, int? excludingId = null, CancellationToken ct = default);
    Task<CustomPatternWriteResult> AddAsync(CustomPatternDefinition definition, CancellationToken ct = default);
    Task<CustomPatternWriteResult> UpdateAsync(CustomPatternDefinition definition, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

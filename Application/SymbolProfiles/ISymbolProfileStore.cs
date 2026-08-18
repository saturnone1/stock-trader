namespace StockTrader.Application.SymbolProfiles;

public interface ISymbolProfileStore
{
    Task<IReadOnlyList<ManagedSymbolProfile>> ListAsync(
        string? normalizedSymbol,
        CancellationToken ct = default);

    Task<ManagedSymbolProfile?> GetActiveAsync(
        string normalizedSymbol,
        CancellationToken ct = default);

    Task<ManagedSymbolProfile?> GetBySymbolAndNameAsync(
        string normalizedSymbol,
        string name,
        CancellationToken ct = default);

    Task<ManagedSymbolProfile> SaveAsync(
        ManagedSymbolProfile profile,
        CancellationToken ct = default);

    Task<ManagedSymbolProfile?> SetActiveAsync(
        long id,
        bool isActive,
        DateTime updatedAt,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}

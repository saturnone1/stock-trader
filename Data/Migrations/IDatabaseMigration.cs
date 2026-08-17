namespace StockTrader.Data.Migrations;

/// <summary>한 번만, ID 순서대로, 트랜잭션 안에서 실행되는 데이터베이스 변경 단위.</summary>
public interface IDatabaseMigration
{
    string Id { get; }
    string Description { get; }
    Task ApplyAsync(SqliteMigrationContext context, CancellationToken ct);
}

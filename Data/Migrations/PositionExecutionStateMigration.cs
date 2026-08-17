namespace StockTrader.Data.Migrations;

/// <summary>실시간 청산 정책의 재시작 안전 상태를 포지션과 함께 영속화한다.</summary>
public sealed class PositionExecutionStateMigration : IDatabaseMigration
{
    public string Id => "202608180002_position_execution_state";
    public string Description => "포지션 원래 위험거리와 보호손절 상태 영속화";

    public Task ApplyAsync(SqliteMigrationContext db, CancellationToken ct) =>
        db.EnsureColumnsAsync("Positions", new Dictionary<string, string>
        {
            ["InitialRiskDistance"] = "REAL NOT NULL DEFAULT 0",
            ["BreakevenApplied"] = "INTEGER NOT NULL DEFAULT 0",
            ["TrailingStopActivated"] = "INTEGER NOT NULL DEFAULT 0",
        }, ct);
}

namespace StockTrader.Data.Migrations;

/// <summary>브로커 청산 주문의 내구성 있는 의도와 주문 ID를 저장한다.</summary>
public sealed class PositionExitIntentMigration : IDatabaseMigration
{
    public string Id => "202608180003_position_exit_intent";
    public string Description => "중복 청산 방지를 위한 포지션 청산 의도 영속화";

    public Task ApplyAsync(SqliteMigrationContext db, CancellationToken ct) =>
        db.EnsureColumnsAsync("Positions", new Dictionary<string, string>
        {
            ["ExitRequestedAt"] = "TEXT",
            ["ExitRequestReason"] = "TEXT",
            ["ExitOrderId"] = "TEXT",
        }, ct);
}

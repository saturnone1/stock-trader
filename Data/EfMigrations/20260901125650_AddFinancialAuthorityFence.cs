using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class AddFinancialAuthorityFence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialAuthorityFences",
                columns: table => new
                {
                    TransitionId = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorityGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    NewEntryAcceptance = table.Column<string>(type: "TEXT", nullable: false),
                    ManualCommandAcceptance = table.Column<string>(type: "TEXT", nullable: false),
                    PositionCycle = table.Column<string>(type: "TEXT", nullable: false),
                    EntryReconciliation = table.Column<string>(type: "TEXT", nullable: false),
                    PositionReconciliation = table.Column<string>(type: "TEXT", nullable: false),
                    LastCompletedPositionBarUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UnresolvedIntentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnresolvedBrokerEffectCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ActivityJournalCount = table.Column<long>(type: "INTEGER", nullable: false),
                    EnabledConsumerLag = table.Column<long>(type: "INTEGER", nullable: false),
                    FenceHash = table.Column<string>(type: "TEXT", nullable: false),
                    IsReleased = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialAuthorityFences", x => x.TransitionId);
                });

            migrationBuilder.CreateTable(
                name: "FinancialAuthorityMirror",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AuthorityGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    Owner = table.Column<string>(type: "TEXT", nullable: false),
                    TransitionId = table.Column<string>(type: "TEXT", nullable: false),
                    ReceiptHash = table.Column<string>(type: "TEXT", nullable: false),
                    MirroredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialAuthorityMirror", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAuthorityFences_IsReleased_AuthorityGeneration",
                table: "FinancialAuthorityFences",
                columns: new[] { "IsReleased", "AuthorityGeneration" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialAuthorityFences");

            migrationBuilder.DropTable(
                name: "FinancialAuthorityMirror");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class AddDurableEntryExecutionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EntryAccountId",
                table: "TradeRecommendations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntryExecutionNote",
                table: "TradeRecommendations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntryOrderId",
                table: "TradeRecommendations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EntryRequestedAt",
                table: "TradeRecommendations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceSignalId",
                table: "TradeRecommendations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeRecommendations_SourceSignalId",
                table: "TradeRecommendations",
                column: "SourceSignalId",
                unique: true,
                filter: "\"SourceSignalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TradeRecommendations_WasExecuted_EntryRequestedAt",
                table: "TradeRecommendations",
                columns: new[] { "WasExecuted", "EntryRequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TradeRecommendations_SourceSignalId",
                table: "TradeRecommendations");

            migrationBuilder.DropIndex(
                name: "IX_TradeRecommendations_WasExecuted_EntryRequestedAt",
                table: "TradeRecommendations");

            migrationBuilder.DropColumn(
                name: "EntryAccountId",
                table: "TradeRecommendations");

            migrationBuilder.DropColumn(
                name: "EntryExecutionNote",
                table: "TradeRecommendations");

            migrationBuilder.DropColumn(
                name: "EntryOrderId",
                table: "TradeRecommendations");

            migrationBuilder.DropColumn(
                name: "EntryRequestedAt",
                table: "TradeRecommendations");

            migrationBuilder.DropColumn(
                name: "SourceSignalId",
                table: "TradeRecommendations");
        }
    }
}

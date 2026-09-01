using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class PersistPositionExecutionContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EntryMarketDataEvidenceJson",
                table: "Positions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutionArtifactJson",
                table: "Positions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEvaluatedBarUtc",
                table: "Positions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastEvaluatedEvidenceId",
                table: "Positions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastEvaluatedMarketDataRevision",
                table: "Positions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EntryMarketDataEvidenceJson",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ExecutionArtifactJson",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "LastEvaluatedBarUtc",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "LastEvaluatedEvidenceId",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "LastEvaluatedMarketDataRevision",
                table: "Positions");
        }
    }
}

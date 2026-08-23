using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class CompleteRemoteOptimizationAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OptimizationWorkerLeases_JobId_Purpose_InputHash",
                table: "OptimizationWorkerLeases");

            migrationBuilder.AddColumn<int>(
                name: "Authority",
                table: "OptimizationWorkerLeases",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanonicalCommittedAt",
                table: "OptimizationWorkerLeases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalResultHash",
                table: "OptimizationWorkerLeases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OptimizationWorkerLeases_JobId_Purpose_InputHash_Authority",
                table: "OptimizationWorkerLeases",
                columns: new[] { "JobId", "Purpose", "InputHash", "Authority" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OptimizationWorkerLeases_JobId_Purpose_InputHash_Authority",
                table: "OptimizationWorkerLeases");

            migrationBuilder.DropColumn(
                name: "Authority",
                table: "OptimizationWorkerLeases");

            migrationBuilder.DropColumn(
                name: "CanonicalCommittedAt",
                table: "OptimizationWorkerLeases");

            migrationBuilder.DropColumn(
                name: "CanonicalResultHash",
                table: "OptimizationWorkerLeases");

            migrationBuilder.CreateIndex(
                name: "IX_OptimizationWorkerLeases_JobId_Purpose_InputHash",
                table: "OptimizationWorkerLeases",
                columns: new[] { "JobId", "Purpose", "InputHash" },
                unique: true);
        }
    }
}

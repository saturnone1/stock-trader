using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class AddOptimizationShadowComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthoritativeResultHash",
                table: "OptimizationWorkerLeases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthoritativeResultJson",
                table: "OptimizationWorkerLeases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ComparedAt",
                table: "OptimizationWorkerLeases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComparisonDetail",
                table: "OptimizationWorkerLeases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ComparisonStatus",
                table: "OptimizationWorkerLeases",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthoritativeResultHash",
                table: "OptimizationWorkerLeases");

            migrationBuilder.DropColumn(
                name: "AuthoritativeResultJson",
                table: "OptimizationWorkerLeases");

            migrationBuilder.DropColumn(
                name: "ComparedAt",
                table: "OptimizationWorkerLeases");

            migrationBuilder.DropColumn(
                name: "ComparisonDetail",
                table: "OptimizationWorkerLeases");

            migrationBuilder.DropColumn(
                name: "ComparisonStatus",
                table: "OptimizationWorkerLeases");
        }
    }
}

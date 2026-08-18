using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class AddPatternSignalBarIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PatternSignals_Symbol_PatternType_DetectedAt",
                table: "PatternSignals");

            migrationBuilder.AddColumn<DateTime>(
                name: "SignalBarAt",
                table: "PatternSignals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatternSignals_Symbol_PatternType_CustomPatternName_SignalBarAt",
                table: "PatternSignals",
                columns: new[] { "Symbol", "PatternType", "CustomPatternName", "SignalBarAt" },
                unique: true,
                filter: "\"CustomPatternName\" IS NOT NULL AND \"SignalBarAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PatternSignals_Symbol_PatternType_SignalBarAt",
                table: "PatternSignals",
                columns: new[] { "Symbol", "PatternType", "SignalBarAt" },
                unique: true,
                filter: "\"CustomPatternName\" IS NULL AND \"SignalBarAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PatternSignals_Symbol_PatternType_CustomPatternName_SignalBarAt",
                table: "PatternSignals");

            migrationBuilder.DropIndex(
                name: "IX_PatternSignals_Symbol_PatternType_SignalBarAt",
                table: "PatternSignals");

            migrationBuilder.DropColumn(
                name: "SignalBarAt",
                table: "PatternSignals");

            migrationBuilder.CreateIndex(
                name: "IX_PatternSignals_Symbol_PatternType_DetectedAt",
                table: "PatternSignals",
                columns: new[] { "Symbol", "PatternType", "DetectedAt" },
                unique: true);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class AddCausalSignalScoringFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SourceSignalId",
                table: "TradeRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceSignalId",
                table: "Positions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ScoringAtrPercent",
                table: "PatternSignals",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ScoringBollingerPosition",
                table: "PatternSignals",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScoringFeatureVersion",
                table: "PatternSignals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ScoringHistoricalWinRate",
                table: "PatternSignals",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ScoringLongTrendHistoryAvailable",
                table: "PatternSignals",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ScoringMarketRegimeCode",
                table: "PatternSignals",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ScoringPriceVsLongMovingAverage",
                table: "PatternSignals",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ScoringRiskRewardRatio",
                table: "PatternSignals",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ScoringRsi",
                table: "PatternSignals",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ScoringVolumeRatio",
                table: "PatternSignals",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeRecords_SourceSignalId",
                table: "TradeRecords",
                column: "SourceSignalId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_SourceSignalId",
                table: "Positions",
                column: "SourceSignalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TradeRecords_SourceSignalId",
                table: "TradeRecords");

            migrationBuilder.DropIndex(
                name: "IX_Positions_SourceSignalId",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "SourceSignalId",
                table: "TradeRecords");

            migrationBuilder.DropColumn(
                name: "SourceSignalId",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ScoringAtrPercent",
                table: "PatternSignals");

            migrationBuilder.DropColumn(
                name: "ScoringBollingerPosition",
                table: "PatternSignals");

            migrationBuilder.DropColumn(
                name: "ScoringFeatureVersion",
                table: "PatternSignals");

            migrationBuilder.DropColumn(
                name: "ScoringHistoricalWinRate",
                table: "PatternSignals");

            migrationBuilder.DropColumn(
                name: "ScoringLongTrendHistoryAvailable",
                table: "PatternSignals");

            migrationBuilder.DropColumn(
                name: "ScoringMarketRegimeCode",
                table: "PatternSignals");

            migrationBuilder.DropColumn(
                name: "ScoringPriceVsLongMovingAverage",
                table: "PatternSignals");

            migrationBuilder.DropColumn(
                name: "ScoringRiskRewardRatio",
                table: "PatternSignals");

            migrationBuilder.DropColumn(
                name: "ScoringRsi",
                table: "PatternSignals");

            migrationBuilder.DropColumn(
                name: "ScoringVolumeRatio",
                table: "PatternSignals");
        }
    }
}

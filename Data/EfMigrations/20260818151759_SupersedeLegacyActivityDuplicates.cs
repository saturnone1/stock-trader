using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class SupersedeLegacyActivityDuplicates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSuperseded",
                table: "TradeRecommendations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuperseded",
                table: "PatternSignals",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Before SignalBarAt/SourceSignalId existed, the daily scanner could persist the same
            // observed setup repeatedly. Preserve every row for audit, but mark all except the
            // latest safe, unexecuted activity for the same UTC day and exact trading geometry.
            migrationBuilder.Sql(
                """
                UPDATE "PatternSignals" AS "older"
                SET "IsSuperseded" = 1
                WHERE "older"."SignalBarAt" IS NULL
                  AND EXISTS (
                    SELECT 1
                    FROM "PatternSignals" AS "newer"
                    WHERE "newer"."SignalBarAt" IS NULL
                      AND "newer"."Symbol" = "older"."Symbol"
                      AND "newer"."PatternType" = "older"."PatternType"
                      AND "newer"."CustomPatternName" IS "older"."CustomPatternName"
                      AND "newer"."EntryPrice" = "older"."EntryPrice"
                      AND "newer"."StopLossPrice" = "older"."StopLossPrice"
                      AND "newer"."TargetPrice" = "older"."TargetPrice"
                      AND date("newer"."DetectedAt") = date("older"."DetectedAt")
                      AND (
                        "newer"."DetectedAt" > "older"."DetectedAt"
                        OR (
                          "newer"."DetectedAt" = "older"."DetectedAt"
                          AND "newer"."Id" > "older"."Id")))
                """);

            migrationBuilder.Sql(
                """
                UPDATE "TradeRecommendations" AS "older"
                SET "IsSuperseded" = 1
                WHERE "older"."SourceSignalId" IS NULL
                  AND "older"."WasExecuted" = 0
                  AND "older"."EntryRequestedAt" IS NULL
                  AND COALESCE("older"."EntryOrderId", '') = ''
                  AND EXISTS (
                    SELECT 1
                    FROM "TradeRecommendations" AS "newer"
                    WHERE "newer"."SourceSignalId" IS NULL
                      AND "newer"."WasExecuted" = 0
                      AND "newer"."EntryRequestedAt" IS NULL
                      AND COALESCE("newer"."EntryOrderId", '') = ''
                      AND "newer"."Symbol" = "older"."Symbol"
                      AND "newer"."PatternType" = "older"."PatternType"
                      AND "newer"."CustomPatternName" IS "older"."CustomPatternName"
                      AND "newer"."EntryPrice" = "older"."EntryPrice"
                      AND "newer"."StopLossPrice" = "older"."StopLossPrice"
                      AND "newer"."TargetPrice" = "older"."TargetPrice"
                      AND "newer"."PositionSize" = "older"."PositionSize"
                      AND "newer"."ShareQuantity" = "older"."ShareQuantity"
                      AND "newer"."Expectancy" = "older"."Expectancy"
                      AND "newer"."Mode" = "older"."Mode"
                      AND date("newer"."GeneratedAt") = date("older"."GeneratedAt")
                      AND (
                        "newer"."GeneratedAt" > "older"."GeneratedAt"
                        OR (
                          "newer"."GeneratedAt" = "older"."GeneratedAt"
                          AND "newer"."Id" > "older"."Id")))
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TradeRecommendations_IsSuperseded_GeneratedAt",
                table: "TradeRecommendations",
                columns: new[] { "IsSuperseded", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PatternSignals_IsActive_IsSuperseded_DetectedAt",
                table: "PatternSignals",
                columns: new[] { "IsActive", "IsSuperseded", "DetectedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TradeRecommendations_IsSuperseded_GeneratedAt",
                table: "TradeRecommendations");

            migrationBuilder.DropIndex(
                name: "IX_PatternSignals_IsActive_IsSuperseded_DetectedAt",
                table: "PatternSignals");

            migrationBuilder.DropColumn(
                name: "IsSuperseded",
                table: "TradeRecommendations");

            migrationBuilder.DropColumn(
                name: "IsSuperseded",
                table: "PatternSignals");
        }
    }
}

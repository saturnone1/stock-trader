using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class AddOptimizationWorkerLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OptimizationWorkerLeases",
                columns: table => new
                {
                    LeaseId = table.Column<string>(type: "TEXT", nullable: false),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    InputHash = table.Column<string>(type: "TEXT", nullable: false),
                    InputJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkerId = table.Column<string>(type: "TEXT", nullable: true),
                    LeaseGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    CancellationGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LeasedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastHeartbeatAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TestedCombinations = table.Column<long>(type: "INTEGER", nullable: false),
                    SubmissionId = table.Column<string>(type: "TEXT", nullable: true),
                    ResultHash = table.Column<string>(type: "TEXT", nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptimizationWorkerLeases", x => x.LeaseId);
                    table.ForeignKey(
                        name: "FK_OptimizationWorkerLeases_OptimizationJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "OptimizationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OptimizationWorkerLeases_JobId_Purpose_InputHash",
                table: "OptimizationWorkerLeases",
                columns: new[] { "JobId", "Purpose", "InputHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OptimizationWorkerLeases_Status_ExpiresAt_CreatedAt",
                table: "OptimizationWorkerLeases",
                columns: new[] { "Status", "ExpiresAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OptimizationWorkerLeases_SubmissionId",
                table: "OptimizationWorkerLeases",
                column: "SubmissionId",
                unique: true,
                filter: "\"SubmissionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OptimizationWorkerLeases");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class AddDurablePositionExecutionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExecutionRequestKind",
                table: "Positions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExecutionRequestRuleIndex",
                table: "Positions",
                type: "INTEGER",
                nullable: true);

            // 기존 계약은 전량·부분 매도를 함께 표현했다. 요청 수량이 현재 보유량보다
            // 작으면 부분 매도로, 그 외에는 전량 청산으로 손실 없이 승격한다.
            migrationBuilder.Sql(
                """
                UPDATE Positions
                SET ExecutionRequestKind = CASE
                    WHEN ExitRequestQuantity < Quantity THEN 1
                    ELSE 0
                END
                WHERE ExitRequestedAt IS NOT NULL
                  AND ExecutionRequestKind IS NULL;
                """);

            migrationBuilder.CreateTable(
                name: "PositionScalingExecutions",
                columns: table => new
                {
                    PositionId = table.Column<long>(type: "INTEGER", nullable: false),
                    RuleIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    ExecutionCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionScalingExecutions", x => new { x.PositionId, x.RuleIndex });
                    table.ForeignKey(
                        name: "FK_PositionScalingExecutions_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PositionScalingExecutions");

            migrationBuilder.DropColumn(
                name: "ExecutionRequestKind",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ExecutionRequestRuleIndex",
                table: "Positions");
        }
    }
}

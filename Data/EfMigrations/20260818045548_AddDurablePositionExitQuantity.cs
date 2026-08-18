using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class AddDurablePositionExitQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExitRequestMarksPartialProfit",
                table: "Positions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ExitRequestQuantity",
                table: "Positions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InitialQuantity",
                table: "Positions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "PartialProfitTaken",
                table: "Positions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // 기존 포지션은 현재 수량이 원래 수량이며, 기존 대기 청산은 모두 전량 청산이었다.
            // 이 보정으로 배포 직후 재시작해도 기존 청산 의도의 의미가 바뀌지 않는다.
            migrationBuilder.Sql("""
                UPDATE Positions
                SET InitialQuantity = Quantity
                WHERE InitialQuantity = 0;

                UPDATE Positions
                SET ExitRequestQuantity = Quantity
                WHERE ExitRequestedAt IS NOT NULL
                  AND ExitRequestQuantity IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExitRequestMarksPartialProfit",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ExitRequestQuantity",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "InitialQuantity",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "PartialProfitTaken",
                table: "Positions");
        }
    }
}

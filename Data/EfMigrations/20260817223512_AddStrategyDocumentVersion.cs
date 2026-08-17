using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class AddStrategyDocumentVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocumentVersion",
                table: "CustomPatterns",
                type: "INTEGER",
                nullable: false,
                defaultValue: StockTrader.Domain.Strategies.StrategyDocumentVersions.Current);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentVersion",
                table: "CustomPatterns");
        }
    }
}

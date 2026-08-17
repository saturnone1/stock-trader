using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class AddNormalizedCustomPatternName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomPatterns_Name",
                table: "CustomPatterns");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "CustomPatterns",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Existing display names were already trimmed by the application. Populate the new
            // comparison key before creating its unique index; index creation intentionally fails
            // closed if an older database contains a case-only duplicate.
            migrationBuilder.Sql(
                "UPDATE \"CustomPatterns\" SET \"NormalizedName\" = upper(trim(\"Name\"));");

            migrationBuilder.CreateIndex(
                name: "IX_CustomPatterns_NormalizedName",
                table: "CustomPatterns",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomPatterns_NormalizedName",
                table: "CustomPatterns");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "CustomPatterns");

            migrationBuilder.CreateIndex(
                name: "IX_CustomPatterns_Name",
                table: "CustomPatterns",
                column: "Name",
                unique: true);
        }
    }
}

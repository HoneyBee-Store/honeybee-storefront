using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HoneyBee.Web.Migrations
{
    /// <inheritdoc />
    public partial class CartAndOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SizeKg",
                table: "OrderItems",
                type: "decimal(6,3)",
                precision: 6,
                scale: 3,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SizeKg",
                table: "OrderItems");
        }
    }
}

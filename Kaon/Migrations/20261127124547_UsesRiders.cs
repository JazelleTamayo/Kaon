using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kaon.Migrations
{
    /// <inheritdoc />
    public partial class UsesRiders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UsesRiders",
                table: "StoreSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UsesRiders",
                table: "StoreSettings");
        }
    }
}

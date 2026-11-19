using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kaon.Migrations
{
    /// <inheritdoc />
    public partial class MultiCarinderia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "JoinedStoreId",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "StoreSettings",
                type: "TEXT",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "Orders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "MenuItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Everything that existed before belongs to the one carinderia that already exists
            migrationBuilder.Sql("UPDATE StoreSettings SET Slug = 'carinderia-' || Id WHERE Slug = ''");
            migrationBuilder.Sql("UPDATE MenuItems SET StoreId = (SELECT MIN(Id) FROM StoreSettings)");
            migrationBuilder.Sql("UPDATE Orders SET StoreId = (SELECT MIN(Id) FROM StoreSettings)");
            migrationBuilder.Sql("UPDATE Users SET StoreId = (SELECT MIN(Id) FROM StoreSettings) WHERE Role = 'Owner'");
            migrationBuilder.Sql("UPDATE Users SET JoinedStoreId = (SELECT MIN(Id) FROM StoreSettings) WHERE Role = 'Customer'");

            migrationBuilder.CreateTable(
                name: "CustomerStores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerStores", x => x.Id);
                });

            migrationBuilder.Sql("INSERT INTO CustomerStores (UserId, StoreId, JoinedAt) SELECT Id, (SELECT MIN(Id) FROM StoreSettings), datetime('now') FROM Users WHERE Role = 'Customer' AND (SELECT COUNT(*) FROM StoreSettings) > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Users_StoreId",
                table: "Users",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreSettings_Slug",
                table: "StoreSettings",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId",
                table: "Orders",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_StoreId",
                table: "MenuItems",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerStores_UserId_StoreId",
                table: "CustomerStores",
                columns: new[] { "UserId", "StoreId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerStores");

            migrationBuilder.DropIndex(
                name: "IX_Users_StoreId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_StoreSettings_Slug",
                table: "StoreSettings");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StoreId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_MenuItems_StoreId",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "JoinedStoreId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "MenuItems");
        }
    }
}

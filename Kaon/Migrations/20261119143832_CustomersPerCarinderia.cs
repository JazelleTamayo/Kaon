using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kaon.Migrations
{
    /// <inheritdoc />
    public partial class CustomersPerCarinderia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Each customer now belongs to ONE carinderia: the one they signed up at (or the first they joined)
            migrationBuilder.Sql("UPDATE Users SET StoreId = COALESCE(JoinedStoreId, (SELECT StoreId FROM CustomerStores c WHERE c.UserId = Users.Id ORDER BY c.JoinedAt, c.Id LIMIT 1), (SELECT MIN(Id) FROM StoreSettings)) WHERE Role = 'Customer' AND StoreId IS NULL");

            migrationBuilder.DropTable(
                name: "CustomerStores");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "JoinedStoreId",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email_StoreId",
                table: "Users",
                columns: new[] { "Email", "StoreId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email_StoreId",
                table: "Users");

            migrationBuilder.AddColumn<int>(
                name: "JoinedStoreId",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerStores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JoinedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerStores", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerStores_UserId_StoreId",
                table: "CustomerStores",
                columns: new[] { "UserId", "StoreId" },
                unique: true);
        }
    }
}

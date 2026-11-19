using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kaon.Migrations
{
    /// <inheritdoc />
    public partial class CarinderiaRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarinderiaRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CarinderiaName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OwnerName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarinderiaRequests", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarinderiaRequests");
        }
    }
}

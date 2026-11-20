using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kaon.Migrations
{
    /// <inheritdoc />
    public partial class Automation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderForAccessUntil",
                table: "Subscriptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderStage",
                table: "Subscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AutoCopyMenu",
                table: "StoreSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAutoMenuOn",
                table: "StoreSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderForAccessUntil",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ReminderStage",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "AutoCopyMenu",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "LastAutoMenuOn",
                table: "StoreSettings");
        }
    }
}

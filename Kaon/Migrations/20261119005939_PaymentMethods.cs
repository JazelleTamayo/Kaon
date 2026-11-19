using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kaon.Migrations
{
    /// <inheritdoc />
    public partial class PaymentMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AcceptsCash",
                table: "StoreSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AcceptsCashless",
                table: "StoreSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "GCashName",
                table: "StoreSettings",
                type: "TEXT",
                maxLength: 80,
                nullable: true,
                defaultValue: "Jamaica Foods");

            migrationBuilder.AddColumn<string>(
                name: "GCashNumber",
                table: "StoreSettings",
                type: "TEXT",
                maxLength: 30,
                nullable: true,
                defaultValue: "09XX XXX XXXX");

            migrationBuilder.AddColumn<string>(
                name: "PaymentNote",
                table: "StoreSettings",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "Orders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PaymentProof",
                table: "Orders",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentRef",
                table: "Orders",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentStatus",
                table: "Orders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptsCash",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "AcceptsCashless",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "GCashName",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "GCashNumber",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PaymentNote",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentProof",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentRef",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Orders");
        }
    }
}

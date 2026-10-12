using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRefundColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "refund_reason",
                table: "PAYMENT_TRANSACTION",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "refunded_amount",
                table: "PAYMENT_TRANSACTION",
                type: "DECIMAL(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "refunded_at",
                table: "PAYMENT_TRANSACTION",
                type: "DATETIME2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refund_reason",
                table: "PAYMENT_TRANSACTION");

            migrationBuilder.DropColumn(
                name: "refunded_amount",
                table: "PAYMENT_TRANSACTION");

            migrationBuilder.DropColumn(
                name: "refunded_at",
                table: "PAYMENT_TRANSACTION");
        }
    }
}

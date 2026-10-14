using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEscrowDepositPurposeAndAppeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PAYMENT_TRANSACTION_purpose",
                table: "PAYMENT_TRANSACTION");

            migrationBuilder.AlterColumn<string>(
                name: "purpose",
                table: "PAYMENT_TRANSACTION",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(12)",
                oldUnicode: false,
                oldMaxLength: 12);

            migrationBuilder.AddColumn<int>(
                name: "agency_id",
                table: "PAYMENT_TRANSACTION",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ESCROW_APPEAL",
                columns: table => new
                {
                    appeal_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    escrow_txn_id = table.Column<long>(type: "bigint", nullable: false),
                    agency_id = table.Column<int>(type: "int", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    decided_by_admin_id = table.Column<int>(type: "int", nullable: true),
                    decision_note = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    decided_at = table.Column<DateTime>(type: "DATETIME2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ESCROW_APPEAL", x => x.appeal_id);
                    table.ForeignKey(
                        name: "FK_ESCROW_APPEAL_ADMIN_decided_by_admin_id",
                        column: x => x.decided_by_admin_id,
                        principalTable: "ADMIN",
                        principalColumn: "admin_id");
                    table.ForeignKey(
                        name: "FK_ESCROW_APPEAL_ESCROW_TRANSACTION_escrow_txn_id",
                        column: x => x.escrow_txn_id,
                        principalTable: "ESCROW_TRANSACTION",
                        principalColumn: "escrow_txn_id");
                    table.ForeignKey(
                        name: "FK_ESCROW_APPEAL_PARTNER_AGENCY_agency_id",
                        column: x => x.agency_id,
                        principalTable: "PARTNER_AGENCY",
                        principalColumn: "agency_id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_TRANSACTION_agency_id",
                table: "PAYMENT_TRANSACTION",
                column: "agency_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PAYMENT_TRANSACTION_purpose",
                table: "PAYMENT_TRANSACTION",
                sql: "([purpose] = 'ORDER' AND [order_id] IS NOT NULL AND [extension_id] IS NULL AND [subscription_id] IS NULL AND [agency_id] IS NULL) OR ([purpose] = 'EXTENSION' AND [order_id] IS NULL AND [extension_id] IS NOT NULL AND [subscription_id] IS NULL AND [agency_id] IS NULL) OR ([purpose] = 'SUBSCRIPTION' AND [order_id] IS NULL AND [extension_id] IS NULL AND [subscription_id] IS NOT NULL AND [agency_id] IS NULL) OR ([purpose] = 'ESCROW_DEPOSIT' AND [order_id] IS NULL AND [extension_id] IS NULL AND [subscription_id] IS NULL AND [agency_id] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ESCROW_APPEAL_agency_id",
                table: "ESCROW_APPEAL",
                column: "agency_id");

            migrationBuilder.CreateIndex(
                name: "IX_ESCROW_APPEAL_decided_by_admin_id",
                table: "ESCROW_APPEAL",
                column: "decided_by_admin_id");

            migrationBuilder.CreateIndex(
                name: "IX_ESCROW_APPEAL_escrow_txn_id",
                table: "ESCROW_APPEAL",
                column: "escrow_txn_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ESCROW_APPEAL_status_created_at",
                table: "ESCROW_APPEAL",
                columns: new[] { "status", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_PAYMENT_TRANSACTION_PARTNER_AGENCY_agency_id",
                table: "PAYMENT_TRANSACTION",
                column: "agency_id",
                principalTable: "PARTNER_AGENCY",
                principalColumn: "agency_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PAYMENT_TRANSACTION_PARTNER_AGENCY_agency_id",
                table: "PAYMENT_TRANSACTION");

            migrationBuilder.DropTable(
                name: "ESCROW_APPEAL");

            migrationBuilder.DropIndex(
                name: "IX_PAYMENT_TRANSACTION_agency_id",
                table: "PAYMENT_TRANSACTION");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PAYMENT_TRANSACTION_purpose",
                table: "PAYMENT_TRANSACTION");

            migrationBuilder.DropColumn(
                name: "agency_id",
                table: "PAYMENT_TRANSACTION");

            migrationBuilder.AlterColumn<string>(
                name: "purpose",
                table: "PAYMENT_TRANSACTION",
                type: "varchar(12)",
                unicode: false,
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldUnicode: false,
                oldMaxLength: 20);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PAYMENT_TRANSACTION_purpose",
                table: "PAYMENT_TRANSACTION",
                sql: "([purpose] = 'ORDER' AND [order_id] IS NOT NULL AND [extension_id] IS NULL AND [subscription_id] IS NULL) OR ([purpose] = 'EXTENSION' AND [order_id] IS NULL AND [extension_id] IS NOT NULL AND [subscription_id] IS NULL) OR ([purpose] = 'SUBSCRIPTION' AND [order_id] IS NULL AND [extension_id] IS NULL AND [subscription_id] IS NOT NULL)");
        }
    }
}

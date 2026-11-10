using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "advance_amount",
                table: "PAYOUT_ITEM",
                type: "DECIMAL(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "carry_in_amount",
                table: "PAYOUT_ITEM",
                type: "DECIMAL(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "LEDGER_ENTRY",
                columns: table => new
                {
                    entry_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    entry_code = table.Column<string>(type: "nvarchar(450)", nullable: false, computedColumnSql: "('LG' + RIGHT('00000000' + CAST([entry_id] AS VARCHAR(20)), 8))", stored: true),
                    from_party_type = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    from_party_id = table.Column<int>(type: "int", nullable: true),
                    to_party_type = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    to_party_id = table.Column<int>(type: "int", nullable: true),
                    amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    entry_type = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ref_type = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ref_id = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: true),
                    available_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    idempotency_key = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    note = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    created_by_admin_id = table.Column<int>(type: "int", nullable: true),
                    prev_hash = table.Column<string>(type: "CHAR(64)", nullable: false),
                    entry_hash = table.Column<string>(type: "CHAR(64)", nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LEDGER_ENTRY", x => x.entry_id);
                    table.CheckConstraint("CK_LEDGER_ENTRY_amount", "[amount] > 0");
                    table.CheckConstraint("CK_LEDGER_ENTRY_parties", "NOT ([from_party_type] = [to_party_type] AND ISNULL([from_party_id], -1) = ISNULL([to_party_id], -1))");
                });

            migrationBuilder.CreateTable(
                name: "WITHDRAWAL_REQUEST",
                columns: table => new
                {
                    withdrawal_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    worker_id = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    request_status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    bank_account_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    bank_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    reject_reason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reviewed_by = table.Column<int>(type: "int", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    requested_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WITHDRAWAL_REQUEST", x => x.withdrawal_id);
                    table.CheckConstraint("CK_WITHDRAWAL_REQUEST_amount", "[amount] > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LEDGER_ENTRY_entry_code",
                table: "LEDGER_ENTRY",
                column: "entry_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LEDGER_ENTRY_entry_type_created_at",
                table: "LEDGER_ENTRY",
                columns: new[] { "entry_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_LEDGER_ENTRY_from_party_type_from_party_id_created_at",
                table: "LEDGER_ENTRY",
                columns: new[] { "from_party_type", "from_party_id", "created_at" })
                .Annotation("SqlServer:Include", new[] { "amount" });

            migrationBuilder.CreateIndex(
                name: "IX_LEDGER_ENTRY_idempotency_key",
                table: "LEDGER_ENTRY",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LEDGER_ENTRY_order_id",
                table: "LEDGER_ENTRY",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_LEDGER_ENTRY_to_party_type_to_party_id_created_at",
                table: "LEDGER_ENTRY",
                columns: new[] { "to_party_type", "to_party_id", "created_at" })
                .Annotation("SqlServer:Include", new[] { "amount", "available_at", "order_id" });

            migrationBuilder.CreateIndex(
                name: "UX_WITHDRAWAL_REQUEST_worker_pending",
                table: "WITHDRAWAL_REQUEST",
                column: "worker_id",
                unique: true,
                filter: "[request_status] = 'PENDING'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LEDGER_ENTRY");

            migrationBuilder.DropTable(
                name: "WITHDRAWAL_REQUEST");

            migrationBuilder.DropColumn(
                name: "advance_amount",
                table: "PAYOUT_ITEM");

            migrationBuilder.DropColumn(
                name: "carry_in_amount",
                table: "PAYOUT_ITEM");
        }
    }
}

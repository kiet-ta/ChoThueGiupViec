using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerBankChangeRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WORKER_BANK_CHANGE_REQUEST",
                columns: table => new
                {
                    request_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    worker_id = table.Column<int>(type: "int", nullable: false),
                    bank_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    bank_account_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    previous_bank_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    previous_bank_account_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    request_status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    reject_reason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    reviewed_by = table.Column<int>(type: "int", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    requested_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WORKER_BANK_CHANGE_REQUEST", x => x.request_id);
                    table.ForeignKey(
                        name: "FK_WORKER_BANK_CHANGE_REQUEST_WORKER_worker_id",
                        column: x => x.worker_id,
                        principalTable: "WORKER",
                        principalColumn: "worker_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WORKER_BANK_CHANGE_status_requested",
                table: "WORKER_BANK_CHANGE_REQUEST",
                columns: new[] { "request_status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "IX_WORKER_BANK_CHANGE_worker_requested",
                table: "WORKER_BANK_CHANGE_REQUEST",
                columns: new[] { "worker_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "UX_WORKER_BANK_CHANGE_worker_pending",
                table: "WORKER_BANK_CHANGE_REQUEST",
                column: "worker_id",
                unique: true,
                filter: "[request_status] = 'PENDING'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WORKER_BANK_CHANGE_REQUEST");
        }
    }
}

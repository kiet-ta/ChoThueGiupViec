using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgencySwapWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AGENCY_SWAP_WINDOW",
                columns: table => new
                {
                    assignment_id = table.Column<long>(type: "bigint", nullable: false),
                    agency_id = table.Column<int>(type: "int", nullable: false),
                    opened_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    deadline_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    new_worker_id = table.Column<int>(type: "int", nullable: true),
                    closed_at = table.Column<DateTime>(type: "DATETIME2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AGENCY_SWAP_WINDOW", x => x.assignment_id);
                    table.ForeignKey(
                        name: "FK_AGENCY_SWAP_WINDOW_JOB_ASSIGNMENT_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "JOB_ASSIGNMENT",
                        principalColumn: "assignment_id");
                    table.ForeignKey(
                        name: "FK_AGENCY_SWAP_WINDOW_PARTNER_AGENCY_agency_id",
                        column: x => x.agency_id,
                        principalTable: "PARTNER_AGENCY",
                        principalColumn: "agency_id");
                    table.ForeignKey(
                        name: "FK_AGENCY_SWAP_WINDOW_WORKER_new_worker_id",
                        column: x => x.new_worker_id,
                        principalTable: "WORKER",
                        principalColumn: "worker_id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AGENCY_SWAP_WINDOW_agency_id",
                table: "AGENCY_SWAP_WINDOW",
                column: "agency_id");

            migrationBuilder.CreateIndex(
                name: "IX_AGENCY_SWAP_WINDOW_new_worker_id",
                table: "AGENCY_SWAP_WINDOW",
                column: "new_worker_id");

            migrationBuilder.CreateIndex(
                name: "IX_AGENCY_SWAP_WINDOW_status_deadline_at",
                table: "AGENCY_SWAP_WINDOW",
                columns: new[] { "status", "deadline_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AGENCY_SWAP_WINDOW");
        }
    }
}

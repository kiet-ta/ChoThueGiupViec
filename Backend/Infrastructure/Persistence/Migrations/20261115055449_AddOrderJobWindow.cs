using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderJobWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JOB_ASSIGNMENT_slot_id",
                table: "JOB_ASSIGNMENT");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "end_time",
                table: "JOB_ORDER",
                type: "time(0)",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "start_time",
                table: "JOB_ORDER",
                type: "time(0)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ASSIGNMENT_slot_id",
                table: "JOB_ASSIGNMENT",
                column: "slot_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JOB_ASSIGNMENT_slot_id",
                table: "JOB_ASSIGNMENT");

            migrationBuilder.DropColumn(
                name: "end_time",
                table: "JOB_ORDER");

            migrationBuilder.DropColumn(
                name: "start_time",
                table: "JOB_ORDER");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ASSIGNMENT_slot_id",
                table: "JOB_ASSIGNMENT",
                column: "slot_id",
                unique: true,
                filter: "[assignment_status] <> 'CANCELLED' AND [assignment_status] <> 'CANCELLED_BY_WORKER' AND [assignment_status] <> 'REASSIGNED'");
        }
    }
}

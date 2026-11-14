using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAcceptanceWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "acceptance_reminded_at",
                table: "JOB_ASSIGNMENT",
                type: "DATETIME2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "completion_source",
                table: "JOB_ASSIGNMENT",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "redo_count",
                table: "JOB_ASSIGNMENT",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_at",
                table: "JOB_ASSIGNMENT",
                type: "DATETIME2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ASSIGNMENT_assignment_status_submitted_at",
                table: "JOB_ASSIGNMENT",
                columns: new[] { "assignment_status", "submitted_at" });

            // Jobs already waiting for the customer get a T (their last change), so the 24-hour window and the automatic acceptance apply to them too.
            migrationBuilder.Sql(
                "UPDATE JOB_ASSIGNMENT SET submitted_at = updated_at WHERE assignment_status = 'AWAITING_ACCEPTANCE' AND submitted_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JOB_ASSIGNMENT_assignment_status_submitted_at",
                table: "JOB_ASSIGNMENT");

            migrationBuilder.DropColumn(
                name: "acceptance_reminded_at",
                table: "JOB_ASSIGNMENT");

            migrationBuilder.DropColumn(
                name: "completion_source",
                table: "JOB_ASSIGNMENT");

            migrationBuilder.DropColumn(
                name: "redo_count",
                table: "JOB_ASSIGNMENT");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "JOB_ASSIGNMENT");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContinuousWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "continuation_opened_at",
                table: "JOB_ORDER",
                type: "DATETIME2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "continuation_slot_id",
                table: "JOB_ORDER",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "continuous_work",
                table: "JOB_ORDER",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "continuation_opened_at",
                table: "JOB_ORDER");

            migrationBuilder.DropColumn(
                name: "continuation_slot_id",
                table: "JOB_ORDER");

            migrationBuilder.DropColumn(
                name: "continuous_work",
                table: "JOB_ORDER");
        }
    }
}

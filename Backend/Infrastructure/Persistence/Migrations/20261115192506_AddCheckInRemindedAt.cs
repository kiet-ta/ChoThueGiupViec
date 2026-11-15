using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckInRemindedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "checkin_reminded_at",
                table: "JOB_ASSIGNMENT",
                type: "DATETIME2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "checkin_reminded_at",
                table: "JOB_ASSIGNMENT");
        }
    }
}

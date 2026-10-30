using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerWorkArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "work_area_set_at",
                table: "WORKER",
                type: "DATETIME2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "work_districts",
                table: "WORKER",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "work_lat",
                table: "WORKER",
                type: "DECIMAL(9,6)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "work_lng",
                table: "WORKER",
                type: "DECIMAL(9,6)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "work_radius_km",
                table: "WORKER",
                type: "tinyint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "work_area_set_at",
                table: "WORKER");

            migrationBuilder.DropColumn(
                name: "work_districts",
                table: "WORKER");

            migrationBuilder.DropColumn(
                name: "work_lat",
                table: "WORKER");

            migrationBuilder.DropColumn(
                name: "work_lng",
                table: "WORKER");

            migrationBuilder.DropColumn(
                name: "work_radius_km",
                table: "WORKER");
        }
    }
}

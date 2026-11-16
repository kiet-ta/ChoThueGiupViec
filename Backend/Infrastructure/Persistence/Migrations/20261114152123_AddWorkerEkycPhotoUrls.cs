using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerEkycPhotoUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ekyc_back_cccd_url",
                table: "WORKER",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ekyc_front_cccd_url",
                table: "WORKER",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ekyc_selfie_url",
                table: "WORKER",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ekyc_back_cccd_url",
                table: "WORKER");

            migrationBuilder.DropColumn(
                name: "ekyc_front_cccd_url",
                table: "WORKER");

            migrationBuilder.DropColumn(
                name: "ekyc_selfie_url",
                table: "WORKER");
        }
    }
}

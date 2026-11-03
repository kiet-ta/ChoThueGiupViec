using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSkillAndWorkerSkill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WORKER_SKILL");

            migrationBuilder.DropTable(
                name: "SKILL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SKILL",
                columns: table => new
                {
                    skill_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    category = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    skill_code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    skill_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SKILL", x => x.skill_id);
                });

            migrationBuilder.CreateTable(
                name: "WORKER_SKILL",
                columns: table => new
                {
                    worker_id = table.Column<int>(type: "int", nullable: false),
                    skill_id = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    is_verified = table.Column<bool>(type: "bit", nullable: false),
                    years_of_experience = table.Column<decimal>(type: "DECIMAL(3,1)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WORKER_SKILL", x => new { x.worker_id, x.skill_id });
                    table.ForeignKey(
                        name: "FK_WORKER_SKILL_SKILL_skill_id",
                        column: x => x.skill_id,
                        principalTable: "SKILL",
                        principalColumn: "skill_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WORKER_SKILL_WORKER_worker_id",
                        column: x => x.worker_id,
                        principalTable: "WORKER",
                        principalColumn: "worker_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SKILL_skill_code",
                table: "SKILL",
                column: "skill_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WORKER_SKILL_skill_id",
                table: "WORKER_SKILL",
                column: "skill_id");
        }
    }
}

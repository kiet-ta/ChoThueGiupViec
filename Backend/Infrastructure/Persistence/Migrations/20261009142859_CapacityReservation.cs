using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CapacityReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CapacityReservations",
                columns: table => new
                {
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    SlotId = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityReservations", x => x.ReservationId);
                    table.ForeignKey(
                        name: "FK_CapacityReservations_BOOKING_SLOT_SlotId",
                        column: x => x.SlotId,
                        principalTable: "BOOKING_SLOT",
                        principalColumn: "slot_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CapacityReservations_PARTNER_AGENCY_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "PARTNER_AGENCY",
                        principalColumn: "agency_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityReservations_AgencyId",
                table: "CapacityReservations",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityReservations_SlotId",
                table: "CapacityReservations",
                column: "SlotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapacityReservations");
        }
    }
}

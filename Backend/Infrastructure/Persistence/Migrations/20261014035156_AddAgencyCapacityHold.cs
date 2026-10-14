using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgencyCapacityHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AGENCY_CAPACITY_HOLD",
                columns: table => new
                {
                    hold_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    reservation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    agency_id = table.Column<int>(type: "int", nullable: false),
                    slot_id = table.Column<int>(type: "int", nullable: false),
                    commission_rate = table.Column<decimal>(type: "DECIMAL(5,3)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AGENCY_CAPACITY_HOLD", x => x.hold_id);
                    table.ForeignKey(
                        name: "FK_AGENCY_CAPACITY_HOLD_BOOKING_SLOT_slot_id",
                        column: x => x.slot_id,
                        principalTable: "BOOKING_SLOT",
                        principalColumn: "slot_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AGENCY_CAPACITY_HOLD_JOB_ORDER_order_id",
                        column: x => x.order_id,
                        principalTable: "JOB_ORDER",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AGENCY_CAPACITY_HOLD_PARTNER_AGENCY_agency_id",
                        column: x => x.agency_id,
                        principalTable: "PARTNER_AGENCY",
                        principalColumn: "agency_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AGENCY_CAPACITY_HOLD_agency_id",
                table: "AGENCY_CAPACITY_HOLD",
                column: "agency_id");

            migrationBuilder.CreateIndex(
                name: "IX_AGENCY_CAPACITY_HOLD_order_id",
                table: "AGENCY_CAPACITY_HOLD",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_AGENCY_CAPACITY_HOLD_reservation_id",
                table: "AGENCY_CAPACITY_HOLD",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "IX_AGENCY_CAPACITY_HOLD_slot_id",
                table: "AGENCY_CAPACITY_HOLD",
                column: "slot_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AGENCY_CAPACITY_HOLD");
        }
    }
}

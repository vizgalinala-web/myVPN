using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVPN.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceConnectionEventsAndLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_connection_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VpnAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_connection_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_device_connection_events_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_connection_events_CreatedAt",
                table: "device_connection_events",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_device_connection_events_DeviceId",
                table: "device_connection_events",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_device_connection_events_UserId",
                table: "device_connection_events",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_connection_events");
        }
    }
}

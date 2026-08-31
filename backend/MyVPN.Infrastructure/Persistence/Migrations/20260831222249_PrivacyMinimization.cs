using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVPN.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrivacyMinimization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_connection_events");

            migrationBuilder.DropColumn(
                name: "CreatedByIp",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "RevokedByIp",
                table: "refresh_tokens");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedByIp",
                table: "refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokedByIp",
                table: "refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "device_connection_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VpnAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
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
    }
}

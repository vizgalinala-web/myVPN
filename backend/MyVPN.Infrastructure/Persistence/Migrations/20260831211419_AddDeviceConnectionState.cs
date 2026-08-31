using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVPN.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceConnectionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConnectedAt",
                table: "devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastConnectedServerId",
                table: "devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_devices_LastConnectedServerId",
                table: "devices",
                column: "LastConnectedServerId");

            migrationBuilder.AddForeignKey(
                name: "FK_devices_vpn_servers_LastConnectedServerId",
                table: "devices",
                column: "LastConnectedServerId",
                principalTable: "vpn_servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_devices_vpn_servers_LastConnectedServerId",
                table: "devices");

            migrationBuilder.DropIndex(
                name: "IX_devices_LastConnectedServerId",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "ConnectedAt",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "LastConnectedServerId",
                table: "devices");
        }
    }
}

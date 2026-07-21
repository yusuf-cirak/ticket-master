using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketMaster.Modules.Platform.Migrations
{
    /// <inheritdoc />
    public partial class OutboxRelayColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempts",
                schema: "platform",
                table: "outbox",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "locked_until",
                schema: "platform",
                table: "outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "processed_on",
                schema: "platform",
                table: "outbox",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attempts",
                schema: "platform",
                table: "outbox");

            migrationBuilder.DropColumn(
                name: "locked_until",
                schema: "platform",
                table: "outbox");

            migrationBuilder.DropColumn(
                name: "processed_on",
                schema: "platform",
                table: "outbox");
        }
    }
}

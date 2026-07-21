using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketMaster.Modules.Events.Migrations
{
    /// <inheritdoc />
    public partial class OutboxRelayColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempts",
                schema: "events",
                table: "outbox",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "locked_until",
                schema: "events",
                table: "outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "processed_on",
                schema: "events",
                table: "outbox",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attempts",
                schema: "events",
                table: "outbox");

            migrationBuilder.DropColumn(
                name: "locked_until",
                schema: "events",
                table: "outbox");

            migrationBuilder.DropColumn(
                name: "processed_on",
                schema: "events",
                table: "outbox");
        }
    }
}

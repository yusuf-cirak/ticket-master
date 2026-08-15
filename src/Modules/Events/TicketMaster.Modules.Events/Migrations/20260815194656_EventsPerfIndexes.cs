using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketMaster.Modules.Events.Migrations
{
    /// <inheritdoc />
    public partial class EventsPerfIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_outbox_occurred_on",
                schema: "events",
                table: "outbox",
                column: "occurred_on",
                filter: "processed_on IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id_starts_on",
                schema: "events",
                table: "events",
                columns: new[] { "tenant_id", "starts_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_occurred_on",
                schema: "events",
                table: "outbox");

            migrationBuilder.DropIndex(
                name: "ix_events_tenant_id_starts_on",
                schema: "events",
                table: "events");
        }
    }
}

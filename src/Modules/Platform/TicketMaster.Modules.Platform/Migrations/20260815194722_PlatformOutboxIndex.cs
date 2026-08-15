using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketMaster.Modules.Platform.Migrations
{
    /// <inheritdoc />
    public partial class PlatformOutboxIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_outbox_occurred_on",
                schema: "platform",
                table: "outbox",
                column: "occurred_on",
                filter: "processed_on IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_occurred_on",
                schema: "platform",
                table: "outbox");
        }
    }
}

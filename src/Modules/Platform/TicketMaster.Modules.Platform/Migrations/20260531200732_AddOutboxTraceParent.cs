using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketMaster.Modules.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxTraceParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "trace_parent",
                schema: "platform",
                table: "outbox",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "trace_parent",
                schema: "platform",
                table: "outbox");
        }
    }
}

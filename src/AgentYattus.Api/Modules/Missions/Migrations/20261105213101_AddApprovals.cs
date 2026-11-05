using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentYattus.Api.Modules.Missions.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Conversation",
                schema: "missions",
                table: "Missions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Decision",
                schema: "missions",
                table: "Missions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingApproval",
                schema: "missions",
                table: "Missions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Conversation",
                schema: "missions",
                table: "Missions");

            migrationBuilder.DropColumn(
                name: "Decision",
                schema: "missions",
                table: "Missions");

            migrationBuilder.DropColumn(
                name: "PendingApproval",
                schema: "missions",
                table: "Missions");
        }
    }
}

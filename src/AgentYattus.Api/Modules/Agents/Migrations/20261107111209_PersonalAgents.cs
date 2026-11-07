using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentYattus.Api.Modules.Agents.Migrations
{
    /// <inheritdoc />
    public partial class PersonalAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                schema: "agents",
                table: "CustomRoles",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerId",
                schema: "agents",
                table: "CustomRoles");
        }
    }
}

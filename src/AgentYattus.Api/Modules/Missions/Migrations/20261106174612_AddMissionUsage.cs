using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentYattus.Api.Modules.Missions.Migrations
{
    /// <inheritdoc />
    public partial class AddMissionUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CostMicros",
                schema: "missions",
                table: "Missions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "InputTokens",
                schema: "missions",
                table: "Missions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "OutputTokens",
                schema: "missions",
                table: "Missions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostMicros",
                schema: "missions",
                table: "Missions");

            migrationBuilder.DropColumn(
                name: "InputTokens",
                schema: "missions",
                table: "Missions");

            migrationBuilder.DropColumn(
                name: "OutputTokens",
                schema: "missions",
                table: "Missions");
        }
    }
}

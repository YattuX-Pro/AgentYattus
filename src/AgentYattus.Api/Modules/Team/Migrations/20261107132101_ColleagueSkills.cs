using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentYattus.Api.Modules.Team.Migrations
{
    /// <inheritdoc />
    public partial class ColleagueSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Skills",
                schema: "team",
                table: "Colleagues",
                type: "character varying(420)",
                maxLength: 420,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Skills",
                schema: "team",
                table: "Colleagues");
        }
    }
}

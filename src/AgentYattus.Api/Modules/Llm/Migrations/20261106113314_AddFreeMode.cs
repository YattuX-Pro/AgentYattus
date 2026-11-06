using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentYattus.Api.Modules.Llm.Migrations
{
    /// <inheritdoc />
    public partial class AddFreeMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FreeChain",
                schema: "llm",
                table: "Settings",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "FreeMode",
                schema: "llm",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FreeChain",
                schema: "llm",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "FreeMode",
                schema: "llm",
                table: "Settings");
        }
    }
}

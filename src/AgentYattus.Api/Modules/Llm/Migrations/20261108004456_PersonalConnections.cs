using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentYattus.Api.Modules.Llm.Migrations
{
    /// <inheritdoc />
    public partial class PersonalConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                schema: "llm",
                table: "Credentials",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserModels",
                schema: "llm",
                columns: table => new
                {
                    OwnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserModels", x => x.OwnerId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Credentials_OwnerId",
                schema: "llm",
                table: "Credentials",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserModels",
                schema: "llm");

            migrationBuilder.DropIndex(
                name: "IX_Credentials_OwnerId",
                schema: "llm",
                table: "Credentials");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                schema: "llm",
                table: "Credentials");
        }
    }
}

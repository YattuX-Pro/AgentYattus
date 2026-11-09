using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentYattus.Api.Modules.Missions.Migrations
{
    /// <inheritdoc />
    public partial class ConversationIdentityAndInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorJson",
                schema: "missions",
                table: "Turns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InterlocutorJson",
                schema: "missions",
                table: "Missions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsReply",
                schema: "missions",
                table: "Missions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Drafts",
                schema: "missions",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "text", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drafts", x => new { x.Scope, x.OwnerId });
                });

            migrationBuilder.CreateTable(
                name: "Notices",
                schema: "missions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TurnId = table.Column<long>(type: "bigint", nullable: true),
                    AuthorJson = table.Column<string>(type: "text", nullable: true),
                    Text = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notices_Turns_TurnId",
                        column: x => x.TurnId,
                        principalSchema: "missions",
                        principalTable: "Turns",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notices_OwnerId_ReadAt",
                schema: "missions",
                table: "Notices",
                columns: new[] { "OwnerId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notices_TurnId",
                schema: "missions",
                table: "Notices",
                column: "TurnId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Drafts",
                schema: "missions");

            migrationBuilder.DropTable(
                name: "Notices",
                schema: "missions");

            migrationBuilder.DropColumn(
                name: "AuthorJson",
                schema: "missions",
                table: "Turns");

            migrationBuilder.DropColumn(
                name: "InterlocutorJson",
                schema: "missions",
                table: "Missions");

            migrationBuilder.DropColumn(
                name: "NeedsReply",
                schema: "missions",
                table: "Missions");
        }
    }
}

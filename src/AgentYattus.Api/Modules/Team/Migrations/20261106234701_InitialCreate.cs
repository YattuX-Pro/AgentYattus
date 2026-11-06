using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentYattus.Api.Modules.Team.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "team");

            migrationBuilder.CreateTable(
                name: "Colleagues",
                schema: "team",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PortraitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Traits = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Colleagues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Lines",
                schema: "team",
                columns: table => new
                {
                    Event = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Template = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lines", x => x.Event);
                });

            migrationBuilder.CreateTable(
                name: "Portraits",
                schema: "team",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SuggestedName = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Work = table.Column<byte[]>(type: "bytea", nullable: false),
                    WorkType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Talk = table.Column<byte[]>(type: "bytea", nullable: true),
                    TalkType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Portraits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamSettings",
                schema: "team",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    LeaderPortraitId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaderName = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    ManagerPortraitId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultTeam = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TypingMs = table.Column<int>(type: "integer", nullable: true),
                    PauseMs = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Traits",
                schema: "team",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Label = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Style = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Traits", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Colleagues_ProjectId",
                schema: "team",
                table: "Colleagues",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Colleagues_Seat",
                schema: "team",
                table: "Colleagues",
                columns: new[] { "ProjectId", "Role" },
                unique: true,
                filter: "\"Active\"");

            migrationBuilder.CreateIndex(
                name: "IX_Portraits_Slug",
                schema: "team",
                table: "Portraits",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Colleagues",
                schema: "team");

            migrationBuilder.DropTable(
                name: "Lines",
                schema: "team");

            migrationBuilder.DropTable(
                name: "Portraits",
                schema: "team");

            migrationBuilder.DropTable(
                name: "TeamSettings",
                schema: "team");

            migrationBuilder.DropTable(
                name: "Traits",
                schema: "team");
        }
    }
}

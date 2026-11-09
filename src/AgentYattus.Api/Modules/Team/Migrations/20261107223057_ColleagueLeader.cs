using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentYattus.Api.Modules.Team.Migrations
{
    /// <inheritdoc />
    public partial class ColleagueLeader : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaderName",
                schema: "team",
                table: "TeamSettings");

            migrationBuilder.DropColumn(
                name: "LeaderPortraitId",
                schema: "team",
                table: "TeamSettings");

            migrationBuilder.AddColumn<bool>(
                name: "Leader",
                schema: "team",
                table: "Colleagues",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Colleagues_Leader",
                schema: "team",
                table: "Colleagues",
                column: "ProjectId",
                unique: true,
                filter: "\"Active\" AND \"Leader\"");

            // Les équipes d'avant la leçon 129 gardent un chef, comme la galerie importée : l'Analyste. On le change en recrutant.
            migrationBuilder.Sql("""
                UPDATE team."Colleagues" SET "Leader" = TRUE WHERE "Active" AND "Role" = 'analyste';
                UPDATE team."TeamSettings"
                SET "DefaultTeam" = regexp_replace("DefaultTeam", '(\{"role":"analyste",[^}]*)\}', '\1,"leader":true}')
                WHERE "DefaultTeam" IS NOT NULL AND "DefaultTeam" NOT LIKE '%"leader":true%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Colleagues_Leader",
                schema: "team",
                table: "Colleagues");

            migrationBuilder.DropColumn(
                name: "Leader",
                schema: "team",
                table: "Colleagues");

            migrationBuilder.AddColumn<string>(
                name: "LeaderName",
                schema: "team",
                table: "TeamSettings",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaderPortraitId",
                schema: "team",
                table: "TeamSettings",
                type: "uuid",
                nullable: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentYattus.Api.Modules.Missions.Migrations
{
    /// <inheritdoc />
    public partial class AddMissionOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                schema: "missions",
                table: "Missions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByName",
                schema: "missions",
                table: "Missions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                schema: "missions",
                table: "Missions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Missions_ProjectId",
                schema: "missions",
                table: "Missions",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Missions_ProjectId",
                schema: "missions",
                table: "Missions");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "missions",
                table: "Missions");

            migrationBuilder.DropColumn(
                name: "CreatedByName",
                schema: "missions",
                table: "Missions");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "missions",
                table: "Missions");
        }
    }
}

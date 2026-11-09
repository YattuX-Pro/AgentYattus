using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentYattus.Api.Modules.Team.Migrations
{
    /// <inheritdoc />
    public partial class PortraitSpeechAnchorAndRecruitmentSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MouthX",
                schema: "team",
                table: "Portraits",
                type: "double precision",
                nullable: false,
                defaultValue: 0.56000000000000005);

            migrationBuilder.AddColumn<double>(
                name: "MouthY",
                schema: "team",
                table: "Portraits",
                type: "double precision",
                nullable: false,
                defaultValue: 0.37);

            migrationBuilder.AddColumn<Guid>(
                name: "RecruitmentSource",
                schema: "team",
                table: "Colleagues",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Colleagues_RecruitmentSource",
                schema: "team",
                table: "Colleagues",
                column: "RecruitmentSource",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Colleagues_RecruitmentSource",
                schema: "team",
                table: "Colleagues");

            migrationBuilder.DropColumn(
                name: "MouthX",
                schema: "team",
                table: "Portraits");

            migrationBuilder.DropColumn(
                name: "MouthY",
                schema: "team",
                table: "Portraits");

            migrationBuilder.DropColumn(
                name: "RecruitmentSource",
                schema: "team",
                table: "Colleagues");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentYattus.Api.Modules.Settings.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "settings");

            migrationBuilder.CreateTable(
                name: "Instance",
                schema: "settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    MaxIterations = table.Column<int>(type: "integer", nullable: true),
                    MaxDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    MaxConcurrentMissions = table.Column<int>(type: "integer", nullable: true),
                    MaxFileKb = table.Column<int>(type: "integer", nullable: true),
                    ExcludedFiles = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: true),
                    AuditRetentionDays = table.Column<int>(type: "integer", nullable: true),
                    MonthlyBudgetMicros = table.Column<long>(type: "bigint", nullable: true),
                    BudgetAlertPercent = table.Column<int>(type: "integer", nullable: true),
                    BlockAtBudget = table.Column<bool>(type: "boolean", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instance", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Instance",
                schema: "settings");
        }
    }
}

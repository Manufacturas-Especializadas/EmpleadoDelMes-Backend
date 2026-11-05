using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmpleadoDelMes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignAutomationConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CampaignAutomationAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PreviousValues = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    NewValues = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignAutomationAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CampaignAutomationConfiguration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Calendar_NominationOpeningDayOffset = table.Column<int>(type: "int", nullable: true),
                    Calendar_NominationOpeningLocalTime = table.Column<TimeOnly>(type: "time(0)", nullable: true),
                    Calendar_NominationClosingDayOffset = table.Column<int>(type: "int", nullable: true),
                    Calendar_NominationClosingLocalTime = table.Column<TimeOnly>(type: "time(0)", nullable: true),
                    Calendar_OpinionOpeningDayOffset = table.Column<int>(type: "int", nullable: true),
                    Calendar_OpinionOpeningLocalTime = table.Column<TimeOnly>(type: "time(0)", nullable: true),
                    Calendar_OpinionClosingDayOffset = table.Column<int>(type: "int", nullable: true),
                    Calendar_OpinionClosingLocalTime = table.Column<TimeOnly>(type: "time(0)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignAutomationConfiguration", x => x.Id);
                    table.CheckConstraint("CK_Automation_Singleton", "[Id] = 1");
                });

            migrationBuilder.InsertData(
                table: "CampaignAutomationConfiguration",
                columns: new[] { "Id", "IsEnabled", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[] { 1, false, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAutomationAudit_ChangedAtUtc",
                table: "CampaignAutomationAudit",
                column: "ChangedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignAutomationAudit");

            migrationBuilder.DropTable(
                name: "CampaignAutomationConfiguration");
        }
    }
}

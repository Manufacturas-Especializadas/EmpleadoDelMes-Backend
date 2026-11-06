using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmpleadoDelMes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignEvaluators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CampaignEvaluators",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignEvaluators", x => new { x.CampaignId, x.CandidateEmployeeId });
                    table.ForeignKey(
                        name: "FK_CampaignEvaluators_Preselections_CampaignId_CandidateEmployeeId",
                        columns: x => new { x.CampaignId, x.CandidateEmployeeId },
                        principalTable: "Preselections",
                        principalColumns: new[] { "CampaignId", "CandidateEmployeeId" });
                    table.ForeignKey(
                        name: "FK_CampaignEvaluators_SponsorEmployeeAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "SponsorEmployeeAssignments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignEvaluators_AssignmentId",
                table: "CampaignEvaluators",
                column: "AssignmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignEvaluators");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmpleadoDelMes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSponsorEmployeeAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SponsorEmployeeAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SponsorEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SponsorEmployeeAssignments", x => x.Id);
                    table.CheckConstraint("CK_SponsorEmployeeAssignments_Dates", "[UpdatedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_SponsorEmployeeAssignments_DifferentEmployees", "[SponsorEmployeeId] <> [EmployeeId]");
                    table.ForeignKey(
                        name: "FK_SponsorEmployeeAssignments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SponsorEmployeeAssignments_Employees_SponsorEmployeeId",
                        column: x => x.SponsorEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SponsorEmployeeAssignments_EmployeeId",
                table: "SponsorEmployeeAssignments",
                column: "EmployeeId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SponsorEmployeeAssignments_EmployeeId_CreatedAtUtc",
                table: "SponsorEmployeeAssignments",
                columns: new[] { "EmployeeId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SponsorEmployeeAssignments_SponsorEmployeeId_IsActive_EmployeeId",
                table: "SponsorEmployeeAssignments",
                columns: new[] { "SponsorEmployeeId", "IsActive", "EmployeeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SponsorEmployeeAssignments");
        }
    }
}

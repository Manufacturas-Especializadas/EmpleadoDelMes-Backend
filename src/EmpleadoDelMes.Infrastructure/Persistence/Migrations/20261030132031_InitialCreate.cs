using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmpleadoDelMes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvaluationMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    NominationMode = table.Column<int>(type: "int", nullable: false),
                    NominationClosingAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    NominationOpeningAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    OpinionMode = table.Column<int>(type: "int", nullable: false),
                    OpinionClosingAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    OpinionOpeningAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                    table.CheckConstraint("CK_Campaigns_EvaluationMonth", "DAY([EvaluationMonth]) = 1");
                    table.CheckConstraint("CK_Campaigns_NominationInterval", "([NominationOpeningAtUtc] IS NULL AND [NominationClosingAtUtc] IS NULL) OR ([NominationOpeningAtUtc] IS NOT NULL AND [NominationClosingAtUtc] IS NOT NULL AND [NominationOpeningAtUtc] < [NominationClosingAtUtc])");
                    table.CheckConstraint("CK_Campaigns_NominationMode", "[NominationMode] IN (0, 1, 2) AND ([NominationMode] <> 0 OR ([NominationOpeningAtUtc] IS NOT NULL AND [NominationClosingAtUtc] IS NOT NULL))");
                    table.CheckConstraint("CK_Campaigns_OpinionInterval", "([OpinionOpeningAtUtc] IS NULL AND [OpinionClosingAtUtc] IS NULL) OR ([OpinionOpeningAtUtc] IS NOT NULL AND [OpinionClosingAtUtc] IS NOT NULL AND [OpinionOpeningAtUtc] < [OpinionClosingAtUtc])");
                    table.CheckConstraint("CK_Campaigns_OpinionMode", "[OpinionMode] IN (0, 1, 2) AND ([OpinionMode] <> 0 OR ([OpinionOpeningAtUtc] IS NOT NULL AND [OpinionClosingAtUtc] IS NOT NULL))");
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayrollNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Nominations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SponsorEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SponsorPayrollNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SponsorFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CandidatePayrollNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CandidateFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Qualities = table.Column<int>(type: "int", nullable: false),
                    Justification = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RegisteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    IsCancelled = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nominations", x => x.Id);
                    table.CheckConstraint("CK_Nominations_DifferentEmployees", "[SponsorEmployeeId] <> [CandidateEmployeeId]");
                    table.CheckConstraint("CK_Nominations_Qualities", "[Qualities] > 0 AND ([Qualities] & ~15) = 0");
                    table.ForeignKey(
                        name: "FK_Nominations_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Nominations_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Nominations_Employees_CandidateEmployeeId",
                        column: x => x.CandidateEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Nominations_Employees_SponsorEmployeeId",
                        column: x => x.SponsorEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Preselections",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsSelected = table.Column<bool>(type: "bit", nullable: false),
                    LastChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Preselections", x => new { x.CampaignId, x.CandidateEmployeeId });
                    table.ForeignKey(
                        name: "FK_Preselections_AspNetUsers_LastChangedByUserId",
                        column: x => x.LastChangedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Preselections_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Preselections_Employees_CandidateEmployeeId",
                        column: x => x.CandidateEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "NominationAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NominationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NominationAuditEntries", x => x.Id);
                    table.CheckConstraint("CK_NominationAuditEntries_Action", "[Action] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_NominationAuditEntries_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NominationAuditEntries_Nominations_NominationId",
                        column: x => x.NominationId,
                        principalTable: "Nominations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CandidateOpinions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpinionSource = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateOpinions", x => x.Id);
                    table.CheckConstraint("CK_CandidateOpinions_Source", "[OpinionSource] IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_CandidateOpinions_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CandidateOpinions_Preselections_CampaignId_CandidateEmployeeId",
                        columns: x => new { x.CampaignId, x.CandidateEmployeeId },
                        principalTable: "Preselections",
                        principalColumns: new[] { "CampaignId", "CandidateEmployeeId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_EmployeeId",
                table: "AspNetUsers",
                column: "EmployeeId",
                unique: true,
                filter: "[EmployeeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_EvaluationMonth",
                table: "Campaigns",
                column: "EvaluationMonth",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CandidateOpinions_CampaignId_CandidateEmployeeId_OpinionSource",
                table: "CandidateOpinions",
                columns: new[] { "CampaignId", "CandidateEmployeeId", "OpinionSource" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CandidateOpinions_CreatedByUserId",
                table: "CandidateOpinions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_PayrollNumber",
                table: "Employees",
                column: "PayrollNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NominationAuditEntries_ActorUserId",
                table: "NominationAuditEntries",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NominationAuditEntries_NominationId",
                table: "NominationAuditEntries",
                column: "NominationId");

            migrationBuilder.CreateIndex(
                name: "IX_Nominations_CampaignId_SponsorEmployeeId_CandidateEmployeeId",
                table: "Nominations",
                columns: new[] { "CampaignId", "SponsorEmployeeId", "CandidateEmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Nominations_CandidateEmployeeId",
                table: "Nominations",
                column: "CandidateEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Nominations_CreatedByUserId",
                table: "Nominations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Nominations_SponsorEmployeeId",
                table: "Nominations",
                column: "SponsorEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Preselections_CandidateEmployeeId",
                table: "Preselections",
                column: "CandidateEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Preselections_LastChangedByUserId",
                table: "Preselections",
                column: "LastChangedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "CandidateOpinions");

            migrationBuilder.DropTable(
                name: "NominationAuditEntries");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Preselections");

            migrationBuilder.DropTable(
                name: "Nominations");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropTable(
                name: "Employees");
        }
    }
}

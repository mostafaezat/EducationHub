using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegistrationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GuardianName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GuardianPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GuardianEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Relationship = table.Column<int>(type: "int", nullable: false),
                    StudentName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    GradeLevelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    School = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PreferredSubjects = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedStudentProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegistrationRequests_GradeLevels_GradeLevelId",
                        column: x => x.GradeLevelId,
                        principalTable: "GradeLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RegistrationRequests_StudentProfiles_CreatedStudentProfileId",
                        column: x => x.CreatedStudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationRequests_CreatedStudentProfileId",
                table: "RegistrationRequests",
                column: "CreatedStudentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationRequests_GradeLevelId",
                table: "RegistrationRequests",
                column: "GradeLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationRequests_Pending_Unique",
                table: "RegistrationRequests",
                columns: new[] { "TenantId", "GuardianPhone", "StudentName" },
                unique: true,
                filter: "[Status] = 0 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationRequests_TenantId_GuardianPhone",
                table: "RegistrationRequests",
                columns: new[] { "TenantId", "GuardianPhone" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationRequests_TenantId_Status_CreatedAt",
                table: "RegistrationRequests",
                columns: new[] { "TenantId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegistrationRequests");
        }
    }
}

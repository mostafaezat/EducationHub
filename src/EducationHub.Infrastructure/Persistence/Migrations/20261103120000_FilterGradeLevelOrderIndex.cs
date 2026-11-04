using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationHub.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// BL-016: the per-tenant unique index on GradeLevels.Order ignored soft deletes, so a
    /// soft-deleted grade kept its Order slot and blocked any new grade from reusing it.
    /// The index is rebuilt filtered on <c>[IsDeleted] = 0</c>, the same convention used by
    /// the other soft-delete-aware unique indexes. Additive and data-preserving.
    /// </summary>
    public partial class FilterGradeLevelOrderIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GradeLevels_TenantId_Order",
                table: "GradeLevels");

            migrationBuilder.CreateIndex(
                name: "IX_GradeLevels_TenantId_Order",
                table: "GradeLevels",
                columns: new[] { "TenantId", "Order" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GradeLevels_TenantId_Order",
                table: "GradeLevels");

            migrationBuilder.CreateIndex(
                name: "IX_GradeLevels_TenantId_Order",
                table: "GradeLevels",
                columns: new[] { "TenantId", "Order" },
                unique: true);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "SessionCompensations",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Charges",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "SessionCompensations");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Charges");
        }
    }
}

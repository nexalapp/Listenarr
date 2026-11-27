using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGateImportOnCompleteness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GateImportOnCompleteness",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                // true, not the CLR default EF scaffolds: the gate is on by default, and
                // an existing settings row has to come up with it on rather than silently
                // keeping the old unchecked behaviour.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GateImportOnCompleteness",
                table: "ApplicationSettings");
        }
    }
}

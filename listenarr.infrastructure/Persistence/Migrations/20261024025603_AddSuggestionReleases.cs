using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuggestionReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SuggestionReleaseCheckIntervalSeconds",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.CreateTable(
                name: "SuggestionReleases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Asin = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IndexerName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ReleaseTitle = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    ProtectedResult = table.Column<string>(type: "TEXT", nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuggestionReleases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SuggestionReleases_Asin",
                table: "SuggestionReleases",
                column: "Asin",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SuggestionReleases");

            migrationBuilder.DropColumn(
                name: "SuggestionReleaseCheckIntervalSeconds",
                table: "ApplicationSettings");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoverFeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiscoverBackgroundFetchIntervalMinutes",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<string>(
                name: "DiscoverSettingsJson",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateTable(
                name: "DiscoverItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SectionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    Page = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PostedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SourceNote = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Enriched = table.Column<bool>(type: "INTEGER", nullable: false),
                    Book = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoverItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscoverSections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SectionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SectionName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PagesFetched = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalItems = table.Column<int>(type: "INTEGER", nullable: true),
                    Exhausted = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoverSections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoverItems_Source_Enriched",
                table: "DiscoverItems",
                columns: new[] { "Source", "Enriched" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoverItems_Source_SectionId_ExternalId",
                table: "DiscoverItems",
                columns: new[] { "Source", "SectionId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoverSections_Source_SectionId",
                table: "DiscoverSections",
                columns: new[] { "Source", "SectionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscoverItems");

            migrationBuilder.DropTable(
                name: "DiscoverSections");

            migrationBuilder.DropColumn(
                name: "DiscoverBackgroundFetchIntervalMinutes",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "DiscoverSettingsJson",
                table: "ApplicationSettings");
        }
    }
}

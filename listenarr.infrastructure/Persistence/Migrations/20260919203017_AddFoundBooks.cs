using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFoundBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FoundBooksScanIntervalMinutes",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<string>(
                name: "FoundBooksWatchFolders",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "FoundBooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClusterKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Signature = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WatchFolder = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    BookFolder = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    FilesJson = table.Column<string>(type: "TEXT", nullable: false),
                    AudioFileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalDurationSeconds = table.Column<double>(type: "REAL", nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    DetectedTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DetectedAuthor = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DetectedSeries = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DetectedSeriesPosition = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    DetectedNarrator = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DetectedYear = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    DetectedAsin = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Completeness = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CompletenessReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LibraryStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    MatchedAudiobookId = table.Column<int>(type: "INTEGER", nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    BlockedReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SignatureChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoundBooks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FoundBooks_State",
                table: "FoundBooks",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_FoundBooks_WatchFolder_ClusterKey",
                table: "FoundBooks",
                columns: new[] { "WatchFolder", "ClusterKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "FoundBooksScanIntervalMinutes",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "FoundBooksWatchFolders",
                table: "ApplicationSettings");
        }
    }
}

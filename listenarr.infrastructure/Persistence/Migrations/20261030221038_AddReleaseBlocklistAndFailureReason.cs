using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseBlocklistAndFailureReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailureReason",
                table: "Downloads",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BlockedReleases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AudiobookId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReleaseTitle = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Indexer = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ReleaseId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    DownloadId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    BlockedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedReleases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockedReleases_AudiobookId_Fingerprint",
                table: "BlockedReleases",
                columns: new[] { "AudiobookId", "Fingerprint" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockedReleases");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "Downloads");
        }
    }
}

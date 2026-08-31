using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversionJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConversionArchivePath",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ConversionSourceDisposition",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ConvertMp3ToM4b",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ConversionJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AudiobookId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "None"),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, defaultValue: "Automatic"),
                    ActiveDeduplicationKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Progress = table.Column<double>(type: "REAL", nullable: false),
                    SourceFileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChapterCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputPath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    FailureKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CanRetry = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxAttempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 3),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LeaseGeneration = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    EnqueuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversionJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversionJobs_ActiveDeduplicationKey",
                table: "ConversionJobs",
                column: "ActiveDeduplicationKey",
                unique: true,
                filter: "\"ActiveDeduplicationKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConversionJobs_AudiobookId",
                table: "ConversionJobs",
                column: "AudiobookId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversionJobs_Status_NextAttemptAt_LeaseExpiresAt",
                table: "ConversionJobs",
                columns: new[] { "Status", "NextAttemptAt", "LeaseExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversionJobs");

            migrationBuilder.DropColumn(
                name: "ConversionArchivePath",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "ConversionSourceDisposition",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "ConvertMp3ToM4b",
                table: "ApplicationSettings");
        }
    }
}

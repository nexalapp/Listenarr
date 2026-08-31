using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTagJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmbedCoverArtInTags",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TagMappings",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WriteMetadataTags",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TagJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AudiobookId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "None"),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, defaultValue: "Automatic"),
                    ActiveDeduplicationKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Progress = table.Column<double>(type: "REAL", nullable: false),
                    FileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TagsWritten = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectedTagsJson = table.Column<string>(type: "TEXT", nullable: true),
                    OverriddenValuesJson = table.Column<string>(type: "TEXT", nullable: true),
                    PendingOutputPath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PendingOutputLength = table.Column<long>(type: "INTEGER", nullable: true),
                    PendingDestinationPath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PendingFileId = table.Column<int>(type: "INTEGER", nullable: true),
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
                    table.PrimaryKey("PK_TagJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TagJobs_ActiveDeduplicationKey",
                table: "TagJobs",
                column: "ActiveDeduplicationKey",
                unique: true,
                filter: "\"ActiveDeduplicationKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TagJobs_AudiobookId",
                table: "TagJobs",
                column: "AudiobookId");

            migrationBuilder.CreateIndex(
                name: "IX_TagJobs_Status_NextAttemptAt_LeaseExpiresAt",
                table: "TagJobs",
                columns: new[] { "Status", "NextAttemptAt", "LeaseExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TagJobs");

            migrationBuilder.DropColumn(
                name: "EmbedCoverArtInTags",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "TagMappings",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "WriteMetadataTags",
                table: "ApplicationSettings");
        }
    }
}

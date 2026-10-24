using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepairPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConversionJobs_Status_NextAttemptAt_LeaseExpiresAt",
                table: "ConversionJobs");

            migrationBuilder.AddColumn<int>(
                name: "CompletedSourceFiles",
                table: "ConversionJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EnhancerVersion",
                table: "ConversionJobs",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ConversionJobs",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Convert");

            migrationBuilder.AddColumn<bool>(
                name: "RepairEnabled",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RepairMusicProtectionEnabled",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "RepairSourceDisposition",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RepairFileCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AudiobookFileId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourcePath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SourceLength = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceLastWriteUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EnhancedPath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    EnhancedLength = table.Column<long>(type: "INTEGER", nullable: false),
                    EnhancerVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepairFileCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversionJobs_Kind_Status_NextAttemptAt_LeaseExpiresAt",
                table: "ConversionJobs",
                columns: new[] { "Kind", "Status", "NextAttemptAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RepairFileCheckpoints_JobId",
                table: "RepairFileCheckpoints",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_RepairFileCheckpoints_JobId_AudiobookFileId",
                table: "RepairFileCheckpoints",
                columns: new[] { "JobId", "AudiobookFileId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepairFileCheckpoints");

            migrationBuilder.DropIndex(
                name: "IX_ConversionJobs_Kind_Status_NextAttemptAt_LeaseExpiresAt",
                table: "ConversionJobs");

            migrationBuilder.DropColumn(
                name: "CompletedSourceFiles",
                table: "ConversionJobs");

            migrationBuilder.DropColumn(
                name: "EnhancerVersion",
                table: "ConversionJobs");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ConversionJobs");

            migrationBuilder.DropColumn(
                name: "RepairEnabled",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "RepairMusicProtectionEnabled",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "RepairSourceDisposition",
                table: "ApplicationSettings");

            migrationBuilder.CreateIndex(
                name: "IX_ConversionJobs_Status_NextAttemptAt_LeaseExpiresAt",
                table: "ConversionJobs",
                columns: new[] { "Status", "NextAttemptAt", "LeaseExpiresAt" });
        }
    }
}

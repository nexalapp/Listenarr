using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudioAuditHeard",
                table: "Audiobooks",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioAuditHeardAuthor",
                table: "Audiobooks",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioAuditHeardNarrator",
                table: "Audiobooks",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioAuditHeardTitle",
                table: "Audiobooks",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioAuditReason",
                table: "Audiobooks",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioAuditVerdict",
                table: "Audiobooks",
                type: "TEXT",
                maxLength: 24,
                nullable: false,
                defaultValue: "NotAudited");

            migrationBuilder.AddColumn<DateTime>(
                name: "AudioAuditedAt",
                table: "Audiobooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AudioAuditOnImport",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioAuditHeard",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioAuditHeardAuthor",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioAuditHeardNarrator",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioAuditHeardTitle",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioAuditReason",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioAuditVerdict",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioAuditedAt",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioAuditOnImport",
                table: "ApplicationSettings");
        }
    }
}

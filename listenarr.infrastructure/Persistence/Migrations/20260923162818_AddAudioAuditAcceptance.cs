using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioAuditAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AudioAuditAcceptedAt",
                table: "Audiobooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioAuditAcceptedIdentity",
                table: "Audiobooks",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudioAuditAcceptedVerdict",
                table: "Audiobooks",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioAuditAcceptedAt",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioAuditAcceptedIdentity",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioAuditAcceptedVerdict",
                table: "Audiobooks");
        }
    }
}

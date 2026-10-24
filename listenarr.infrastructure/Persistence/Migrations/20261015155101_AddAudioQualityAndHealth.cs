using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioQualityAndHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudioQuality",
                table: "Audiobooks",
                type: "TEXT",
                maxLength: 24,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "AudioQualityIdentity",
                table: "Audiobooks",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioQualityMetricsJson",
                table: "Audiobooks",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AudioQualityProbedAt",
                table: "Audiobooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioQualityReason",
                table: "Audiobooks",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HealthCheckedAt",
                table: "Audiobooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HealthNamingClean",
                table: "Audiobooks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HealthNamingReason",
                table: "Audiobooks",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HealthTagsClean",
                table: "Audiobooks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HealthTagsReason",
                table: "Audiobooks",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AudioQualityProbeEnabled",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<double>(
                name: "GenerativeOfferThresholdDb",
                table: "ApplicationSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 25.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioQuality",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioQualityIdentity",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioQualityMetricsJson",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioQualityProbedAt",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioQualityReason",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "HealthCheckedAt",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "HealthNamingClean",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "HealthNamingReason",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "HealthTagsClean",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "HealthTagsReason",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudioQualityProbeEnabled",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "GenerativeOfferThresholdDb",
                table: "ApplicationSettings");
        }
    }
}

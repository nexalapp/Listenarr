using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoGrabAndReleasePreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayloadFingerprint",
                table: "SuggestionReleases",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Preparation",
                table: "SuggestionReleases",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotPrepared");

            migrationBuilder.AddColumn<string>(
                name: "PreparationDetail",
                table: "SuggestionReleases",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreparedAt",
                table: "SuggestionReleases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedNzbUrl",
                table: "SuggestionReleases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedVia",
                table: "SuggestionReleases",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutoGrabSettingsJson",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<bool>(
                name: "PrepareReleasesEnabled",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PrepareReleasesMayUseMeteredIndexes",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayloadFingerprint",
                table: "SuggestionReleases");

            migrationBuilder.DropColumn(
                name: "Preparation",
                table: "SuggestionReleases");

            migrationBuilder.DropColumn(
                name: "PreparationDetail",
                table: "SuggestionReleases");

            migrationBuilder.DropColumn(
                name: "PreparedAt",
                table: "SuggestionReleases");

            migrationBuilder.DropColumn(
                name: "ProtectedNzbUrl",
                table: "SuggestionReleases");

            migrationBuilder.DropColumn(
                name: "ResolvedVia",
                table: "SuggestionReleases");

            migrationBuilder.DropColumn(
                name: "AutoGrabSettingsJson",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "PrepareReleasesEnabled",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "PrepareReleasesMayUseMeteredIndexes",
                table: "ApplicationSettings");
        }
    }
}

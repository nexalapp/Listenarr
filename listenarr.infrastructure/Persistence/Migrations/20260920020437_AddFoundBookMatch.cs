using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFoundBookMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HeardAt",
                table: "FoundBooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeardAuthor",
                table: "FoundBooks",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeardNarrator",
                table: "FoundBooks",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeardTitle",
                table: "FoundBooks",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeardTranscript",
                table: "FoundBooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchAsin",
                table: "FoundBooks",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchAuthor",
                table: "FoundBooks",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MatchConfidence",
                table: "FoundBooks",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchImageUrl",
                table: "FoundBooks",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchSource",
                table: "FoundBooks",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchTitle",
                table: "FoundBooks",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeardAt",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "HeardAuthor",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "HeardNarrator",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "HeardTitle",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "HeardTranscript",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "MatchAsin",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "MatchAuthor",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "MatchConfidence",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "MatchImageUrl",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "MatchSource",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "MatchTitle",
                table: "FoundBooks");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFoundBookImportQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ImportAttempts",
                table: "FoundBooks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImportNotBefore",
                table: "FoundBooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportRequestJson",
                table: "FoundBooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImportStartedAt",
                table: "FoundBooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastImportError",
                table: "FoundBooks",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImportAttempts",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "ImportNotBefore",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "ImportRequestJson",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "ImportStartedAt",
                table: "FoundBooks");

            migrationBuilder.DropColumn(
                name: "LastImportError",
                table: "FoundBooks");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudiobookFileNotFound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NotFoundSinceUtc",
                table: "AudiobookFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AudiobookFiles_NotFoundSinceUtc",
                table: "AudiobookFiles",
                column: "NotFoundSinceUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AudiobookFiles_NotFoundSinceUtc",
                table: "AudiobookFiles");

            migrationBuilder.DropColumn(
                name: "NotFoundSinceUtc",
                table: "AudiobookFiles");
        }
    }
}

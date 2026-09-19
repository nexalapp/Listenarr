using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChapterPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChapterPlanJson",
                table: "AudiobookFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChapterPlanKey",
                table: "AudiobookFiles",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChapterPlannedAt",
                table: "AudiobookFiles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChapterPlanJson",
                table: "AudiobookFiles");

            migrationBuilder.DropColumn(
                name: "ChapterPlanKey",
                table: "AudiobookFiles");

            migrationBuilder.DropColumn(
                name: "ChapterPlannedAt",
                table: "AudiobookFiles");
        }
    }
}

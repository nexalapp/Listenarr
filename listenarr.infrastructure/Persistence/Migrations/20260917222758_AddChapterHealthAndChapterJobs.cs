using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChapterHealthAndChapterJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChapterPlanJson",
                table: "TagJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "TagJobs",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Tags");

            migrationBuilder.AddColumn<int>(
                name: "ChapterCount",
                table: "AudiobookFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ChapterHealth",
                table: "AudiobookFiles",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "ChapterReason",
                table: "AudiobookFiles",
                type: "TEXT",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChapterPlanJson",
                table: "TagJobs");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "TagJobs");

            migrationBuilder.DropColumn(
                name: "ChapterCount",
                table: "AudiobookFiles");

            migrationBuilder.DropColumn(
                name: "ChapterHealth",
                table: "AudiobookFiles");

            migrationBuilder.DropColumn(
                name: "ChapterReason",
                table: "AudiobookFiles");
        }
    }
}

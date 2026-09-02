using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudiobookListenerRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AudibleRatingOverall",
                table: "Audiobooks",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudibleRatingOverallCount",
                table: "Audiobooks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AudibleRatingPerformance",
                table: "Audiobooks",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudibleRatingPerformanceCount",
                table: "Audiobooks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AudibleRatingStory",
                table: "Audiobooks",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudibleRatingStoryCount",
                table: "Audiobooks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudibleReviewCount",
                table: "Audiobooks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AudnexusRating",
                table: "Audiobooks",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudibleRatingOverall",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudibleRatingOverallCount",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudibleRatingPerformance",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudibleRatingPerformanceCount",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudibleRatingStory",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudibleRatingStoryCount",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudibleReviewCount",
                table: "Audiobooks");

            migrationBuilder.DropColumn(
                name: "AudnexusRating",
                table: "Audiobooks");
        }
    }
}

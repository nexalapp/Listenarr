using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesNameDropWords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeriesNameDropWordsJson",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "[\"Series\",\"Trilogy\",\"Saga\",\"Cycle\",\"Sequence\",\"Duology\",\"Quartet\",\"Quintet\",\"Tetralogy\"]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeriesNameDropWordsJson",
                table: "ApplicationSettings");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAbookThankBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AbookThanksPerHour",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.CreateTable(
                name: "AbookThanks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TopicId = table.Column<int>(type: "INTEGER", nullable: false),
                    ThankedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbookThanks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbookThanks_ThankedAt",
                table: "AbookThanks",
                column: "ThankedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbookThanks");

            migrationBuilder.DropColumn(
                name: "AbookThanksPerHour",
                table: "ApplicationSettings");
        }
    }
}

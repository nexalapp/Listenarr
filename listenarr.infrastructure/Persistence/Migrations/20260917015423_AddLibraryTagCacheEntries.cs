using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryTagCacheEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryTagCacheEntries",
                columns: table => new
                {
                    Path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Length = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TagsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryTagCacheEntries", x => x.Path);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryTagCacheEntries");
        }
    }
}

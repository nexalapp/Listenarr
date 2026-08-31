using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNzbKingTokenLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NzbKingApiAccesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KeyFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Query = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    HttpStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    BalanceAfter = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NzbKingApiAccesses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NzbKingKeyStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KeyFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EstimatedBalance = table.Column<int>(type: "INTEGER", nullable: false),
                    LastRefillAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSuccessfulUseAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    KeyDeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NzbKingKeyStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NzbKingApiAccesses_KeyFingerprint_AttemptedAt",
                table: "NzbKingApiAccesses",
                columns: new[] { "KeyFingerprint", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NzbKingKeyStates_KeyFingerprint",
                table: "NzbKingKeyStates",
                column: "KeyFingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NzbKingApiAccesses");

            migrationBuilder.DropTable(
                name: "NzbKingKeyStates");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Renames the audio-audit verdict <c>RuntimeMismatch</c> to <c>Incomplete</c> in
    /// rows that already hold it.
    ///
    /// <para>
    /// The verdict is persisted by name, not by number, so renaming the enum member
    /// orphaned every row written under the old name: EF cannot map the string back and
    /// throws on any query that touches the audiobook, which took the library page down
    /// until the rows were translated. A rename of a persisted-by-name value is a data
    /// change and needs a migration, however much it looks like a refactor.
    /// </para>
    /// </summary>
    public partial class RenameRuntimeMismatchVerdictToIncomplete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE Audiobooks SET AudioAuditVerdict = 'Incomplete' "
                + "WHERE AudioAuditVerdict = 'RuntimeMismatch';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE Audiobooks SET AudioAuditVerdict = 'RuntimeMismatch' "
                + "WHERE AudioAuditVerdict = 'Incomplete';");
        }
    }
}

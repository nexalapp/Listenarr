using Listenarr.Application.Search.NzbKing;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.AbookLink;

/// <summary>
/// Editing a source must not destroy the secrets it already holds.
///
/// The settings blob round-trips through the form: the API redacts it on the way out and
/// restores it on the way in, but only when the form sends the placeholder back. A field
/// that arrives blank or missing overwrites the stored value instead.
/// </summary>
[Trait("Name", "AbookSettingsRoundTripTests")]
[Trait("Category", "AbookLink")]
public sealed class AbookSettingsRoundTripTests : BaseTests
{
    private const string Stored =
        """{"abook_username":"nexal","abook_password":"ENCRYPTED","nzbking_api_key":"THEKEY"}""";

    [Fact]
    public void SendingBackTheRedactedPlaceholderKeepsTheStoredSecrets()
    {
        // What the form now does: keep whatever the API returned and send it back.
        var fromForm =
            $$"""{"abook_username":"nexal","abook_password":"{{ApiResponseRedactor.RedactedValue}}","nzbking_api_key":"{{ApiResponseRedactor.RedactedValue}}"}""";

        var merged = ApiResponseRedactor.MergeAdditionalSettings(Stored, fromForm);

        Assert.Contains("ENCRYPTED", merged, StringComparison.Ordinal);
        Assert.Contains("THEKEY", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlankedPasswordWouldHaveDestroyedIt()
    {
        // The bug this guards: the form used to blank the password on edit, so adding an
        // API key would silently replace a working password with nothing.
        const string blanked =
            """{"abook_username":"nexal","abook_password":"","nzbking_api_key":"THEKEY"}""";

        var merged = ApiResponseRedactor.MergeAdditionalSettings(Stored, blanked);

        Assert.DoesNotContain("ENCRYPTED", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOmittedKeyIsAlsoLost()
    {
        // Omitting is no safer than blanking: the merge only restores what it can see
        // marked as redacted.
        const string omitted = """{"abook_username":"nexal","abook_password":"REDACTED"}""";

        var merged = ApiResponseRedactor.MergeAdditionalSettings(Stored, omitted);

        Assert.DoesNotContain("THEKEY", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void ARealNewSecretStillReplacesTheOldOne()
    {
        const string changed =
            """{"abook_username":"nexal","abook_password":"a-new-password","nzbking_api_key":"THEKEY"}""";

        var merged = ApiResponseRedactor.MergeAdditionalSettings(Stored, changed);

        Assert.Contains("a-new-password", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("ENCRYPTED", merged, StringComparison.Ordinal);
    }

    // A pool of keys is a list of secrets, which the round-trip has to survive one
    // element at a time. Collapsing the list to a single placeholder would lose which
    // element is which, and writing the list through because it is not a string would
    // hand every key back to the page in the clear.
    private const string StoredPool =
        """{"abook_username":"nexal","nzbking_api_keys":["KEYONE","KEYTWO","KEYTHREE"]}""";

    private static string RedactedPool()
    {
        var indexer = new Indexer { Name = "abook.link", AdditionalSettings = StoredPool };
        return ApiResponseRedactor.RedactIndexer(indexer).AdditionalSettings ?? string.Empty;
    }

    [Fact]
    public void EveryKeyInAPoolIsRedactedOnTheWayOut()
    {
        var redacted = RedactedPool();

        Assert.DoesNotContain("KEYONE", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("KEYTWO", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("KEYTHREE", redacted, StringComparison.Ordinal);
        Assert.Contains("nexal", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void SendingBackEveryPlaceholderKeepsEveryKey()
    {
        var merged = ApiResponseRedactor.MergeAdditionalSettings(StoredPool, RedactedPool());

        Assert.Equal(
            ["KEYONE", "KEYTWO", "KEYTHREE"],
            NzbKingSettings.TryGetApiKeys(merged));
    }

    [Fact]
    public void RemovingTheMiddleKeyRemovesThatOneAndNotAnother()
    {
        // The bug a positional merge of a bare placeholder would cause: with the second
        // row deleted the list arrives one shorter, and restoring by position would keep
        // the first two keys and silently discard the third.
        var redacted = RedactedPool();
        var withoutTheSecond = redacted.Replace("\"REDACTED#1\",", string.Empty, StringComparison.Ordinal);

        var merged = ApiResponseRedactor.MergeAdditionalSettings(StoredPool, withoutTheSecond);

        Assert.Equal(["KEYONE", "KEYTHREE"], NzbKingSettings.TryGetApiKeys(merged));
    }

    [Fact]
    public void ReorderingTheListReordersTheKeys()
    {
        const string reordered =
            """{"abook_username":"nexal","nzbking_api_keys":["REDACTED#2","REDACTED#0"]}""";

        var merged = ApiResponseRedactor.MergeAdditionalSettings(StoredPool, reordered);

        Assert.Equal(["KEYTHREE", "KEYONE"], NzbKingSettings.TryGetApiKeys(merged));
    }

    [Fact]
    public void ANewKeyBesideThePlaceholdersIsStoredAlongsideThem()
    {
        const string added =
            """{"nzbking_api_keys":["REDACTED#0","REDACTED#1","REDACTED#2","KEYFOUR"]}""";

        var merged = ApiResponseRedactor.MergeAdditionalSettings(StoredPool, added);

        Assert.Equal(
            ["KEYONE", "KEYTWO", "KEYTHREE", "KEYFOUR"],
            NzbKingSettings.TryGetApiKeys(merged));
    }

    [Fact]
    public void APlaceholderForAKeyThatIsNoLongerStoredIsDroppedNotStoredLiterally()
    {
        // Storing the placeholder text would leave something shaped like a credential in
        // the settings, and NZBKing would be asked with it.
        const string stale = """{"nzbking_api_keys":["REDACTED#0","REDACTED#9"]}""";

        var merged = ApiResponseRedactor.MergeAdditionalSettings(StoredPool, stale);

        Assert.Equal(["KEYONE"], NzbKingSettings.TryGetApiKeys(merged));
        Assert.DoesNotContain("REDACTED", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingTheListClearsTheKeys()
    {
        const string emptied = """{"abook_username":"nexal","nzbking_api_keys":[]}""";

        var merged = ApiResponseRedactor.MergeAdditionalSettings(StoredPool, emptied);

        Assert.Empty(NzbKingSettings.TryGetApiKeys(merged));
        Assert.DoesNotContain("KEYONE", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInstallWithOnlyTheSingleKeyRoundTripsThroughTheArrayForm()
    {
        // The migration case, end to end: the caller is shown the normalised shape, sends
        // its placeholder back, and the merge resolves it against the same normalised
        // shape. Either side normalising alone would lose the key.
        var shown = ApiResponseRedactor
            .RedactIndexer(new Indexer { Name = "abook.link", AdditionalSettings = Stored })
            .AdditionalSettings;

        Assert.DoesNotContain("THEKEY", shown, StringComparison.Ordinal);

        var merged = ApiResponseRedactor.MergeAdditionalSettings(
            NzbKingSettings.Normalise(Stored), shown);

        Assert.Equal(["THEKEY"], NzbKingSettings.TryGetApiKeys(merged));
    }
}

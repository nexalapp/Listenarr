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
}

using Listenarr.Application.Search.AbookLink;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.AbookLink;

[Trait("Name", "AbookSecretProtectionTests")]
[Trait("Category", "AbookLink")]
public sealed class AbookSecretProtectionTests : BaseTests
{
    // Stand-in for data protection. Base64 rather than a prefix, so the plaintext is
    // genuinely not a substring of the result - a marker-plus-plaintext double would let
    // an assertion that the password is not stored in the clear pass vacuously. Throws on
    // foreign input, as the real protector does.
    private const string Marker = "enc1:";

    private static string Protect(string plain) =>
        Marker + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain));

    private static string Unprotect(string value) =>
        value.StartsWith(Marker, StringComparison.Ordinal)
            ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value[Marker.Length..]))
            : throw new InvalidOperationException("not protected by this protector");

    [Fact]
    public void ThePasswordIsEncryptedBeforeItIsStored()
    {
        var json = """{"abook_username":"nexal","abook_password":"hunter2"}""";

        var stored = AbookSecretProtection.Protect(json, Protect, Unprotect);

        Assert.DoesNotContain("hunter2", stored);
        Assert.Equal("hunter2", AbookLinkSettings.Read(stored, Unprotect).Password);
    }

    [Fact]
    public void SavingTwiceDoesNotDoubleEncrypt()
    {
        // The settings blob round-trips through the edit form, so an already-encrypted
        // value arrives back here. Encrypting it again would make it undecryptable.
        var once = AbookSecretProtection.Protect(
            """{"abook_password":"hunter2"}""", Protect, Unprotect);

        var twice = AbookSecretProtection.Protect(once, Protect, Unprotect);

        Assert.Equal(once, twice);
        Assert.Equal("hunter2", AbookLinkSettings.Read(twice, Unprotect).Password);
    }

    [Fact]
    public void OtherSettingsAreLeftAlone()
    {
        var json = """{"abook_username":"nexal","abook_password":"hunter2","nzbking_api_key":"KEY"}""";

        var stored = AbookSecretProtection.Protect(json, Protect, Unprotect);

        Assert.Contains("\"abook_username\":\"nexal\"", stored);
        Assert.Contains("KEY", stored);
    }

    [Fact]
    public void SettingsWithoutAPasswordPassStraightThrough()
    {
        Assert.Equal("""{"mam_id":"abc"}""",
            AbookSecretProtection.Protect("""{"mam_id":"abc"}""", Protect, Unprotect));
        Assert.Null(AbookSecretProtection.Protect(null, Protect, Unprotect));
        Assert.Equal("not json", AbookSecretProtection.Protect("not json", Protect, Unprotect));
    }
}

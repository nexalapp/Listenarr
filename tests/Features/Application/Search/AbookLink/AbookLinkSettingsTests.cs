using Listenarr.Application.Search.AbookLink;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.AbookLink;

[Trait("Name", "AbookLinkSettingsTests")]
[Trait("Category", "AbookLink")]
public sealed class AbookLinkSettingsTests : BaseTests
{
    [Fact]
    public void UsernameAndPasswordAreRead_WithThePasswordDecrypted()
    {
        var json = """{"abook_username":"nexal","abook_password":"ENCRYPTED"}""";

        var credentials = AbookLinkSettings.Read(json, _ => "the-real-password");

        Assert.Equal("nexal", credentials.Username);
        Assert.Equal("the-real-password", credentials.Password);
        Assert.True(credentials.CanSignIn);
    }

    [Fact]
    public void APasswordThatFailsToDecryptIsTreatedAsAbsent()
    {
        // Passing ciphertext through as a password would fail at the forum with a
        // confusing "wrong password" rather than the real problem.
        var json = """{"abook_username":"nexal","abook_password":"corrupt"}""";

        var credentials = AbookLinkSettings.Read(json, _ => throw new InvalidOperationException("bad payload"));

        Assert.Null(credentials.Password);
        Assert.False(credentials.CanSignIn);
    }

    [Fact]
    public void ASuppliedCookieIsEnoughOnItsOwn()
    {
        var json = """{"abook_session_cookie":"SMFCookie1=abc"}""";

        var credentials = AbookLinkSettings.Read(json);

        Assert.True(credentials.HasAnything);
        Assert.False(credentials.CanSignIn);
        Assert.Equal("SMFCookie1=abc", credentials.SessionCookie);
    }

    [Fact]
    public void NothingConfiguredIsReportedAsSuch()
    {
        Assert.False(AbookLinkSettings.Read(null).HasAnything);
        Assert.False(AbookLinkSettings.Read("{}").HasAnything);
        Assert.False(AbookLinkSettings.Read("not json").HasAnything);
    }

    [Fact]
    public void SecretPropertyNamesMatchTheRedactorsSensitiveKeys()
    {
        // The redactor works on substrings of the property name. If these ever stop
        // matching, the values start coming back over the API in the clear.
        Assert.Contains("password", AbookLinkSettings.PasswordProperty, StringComparison.Ordinal);
        Assert.Contains("cookie", AbookLinkSettings.SessionCookieProperty, StringComparison.Ordinal);
    }
}

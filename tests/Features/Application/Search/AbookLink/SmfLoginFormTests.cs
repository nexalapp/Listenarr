using Listenarr.Application.Search.AbookLink;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.AbookLink;

[Trait("Name", "SmfLoginFormTests")]
[Trait("Category", "AbookLink")]
public sealed class SmfLoginFormTests : BaseTests
{
    [Fact]
    public void EveryHiddenFieldIsCarriedAcross_IncludingTheRandomlyNamedSessionToken()
    {
        // SMF names its session token differently per installation, so it cannot be
        // hardcoded - the login post has to echo back whatever the form carried.
        const string html = """
            <form action="index.php?action=login2" method="post">
              <input type="text" name="user">
              <input type="password" name="passwd">
              <input type="hidden" name="e3a4ec046f" value="09e786dee504af7d9b8b2443745c4f66">
              <input type="hidden" name="hash_passwrd" value="">
              <button type="submit">Login</button>
            </form>
            """;

        var fields = SmfLoginForm.ReadHiddenFields(html);

        Assert.Equal("09e786dee504af7d9b8b2443745c4f66", fields["e3a4ec046f"]);
        Assert.Equal(string.Empty, fields["hash_passwrd"]);
        Assert.DoesNotContain("user", fields.Keys);
        Assert.DoesNotContain("passwd", fields.Keys);
    }

    [Fact]
    public void SignedInIsDetectedByTheLogoutLinkNotTheStatusCode()
    {
        // A logged-out SMF page still answers 200, so the status code proves nothing.
        Assert.True(SmfLoginForm.IsSignedIn("""<a href="index.php?action=logout;abc=def">Logout</a>"""));
        Assert.False(SmfLoginForm.IsSignedIn("""<a href="index.php?action=login">Login</a>"""));
        Assert.False(SmfLoginForm.IsSignedIn(null));
    }

    [Fact]
    public void RejectedCredentialsAreDistinguishedFromOtherFailures()
    {
        // A wrong password needs the operator; a timeout does not.
        Assert.True(SmfLoginForm.LooksLikeBadCredentials("That username does not exist."));
        Assert.True(SmfLoginForm.LooksLikeBadCredentials("The password was incorrect."));
        Assert.False(SmfLoginForm.LooksLikeBadCredentials("Gateway timeout"));
    }

    [Fact]
    public void MalformedMarkupYieldsNoFieldsRatherThanThrowing()
    {
        Assert.Empty(SmfLoginForm.ReadHiddenFields(null));
        Assert.Empty(SmfLoginForm.ReadHiddenFields("<html>no form here</html>"));
    }
}

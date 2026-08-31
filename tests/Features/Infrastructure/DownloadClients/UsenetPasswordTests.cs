using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.DownloadClients;

/// <summary>
/// An archive password has to reach the download client, or the release downloads
/// perfectly and then fails to unpack with nothing explaining why.
/// </summary>
[Trait("Name", "UsenetPasswordTests")]
[Trait("Category", "DownloadClientAdapter")]
public sealed class UsenetPasswordTests : BaseTests
{
    private static DownloadClientConfiguration Client() => new()
    {
        Name = "SAB",
        Type = "sabnzbd",
        Host = "localhost",
        Port = 8080
    };

    [Fact]
    public void SabnzbdSendsThePasswordAsItsOwnParameter()
    {
        var parameters = SabnzbdAddRequestPlanner.BuildFileQueryParams(Client(), "A Book", "s3cret");

        Assert.Equal("s3cret", parameters["password"]);

        // Not smuggled into the display name: nzbname is shown in the queue and written
        // to logs, so a password there is a password on screen.
        Assert.Equal("A Book", parameters["nzbname"]);
    }

    [Fact]
    public void SabnzbdOmitsThePasswordWhenThereIsNone()
    {
        // Most releases have none, and sending an empty password is not the same as
        // sending none - SABnzbd would try to unpack with a blank one.
        var parameters = SabnzbdAddRequestPlanner.BuildFileQueryParams(Client(), "A Book");

        Assert.DoesNotContain("password", parameters.Keys);
    }

    [Fact]
    public void SabnzbdOmitsAnEmptyPasswordToo()
    {
        var parameters = SabnzbdAddRequestPlanner.BuildFileQueryParams(Client(), "A Book", "");

        Assert.DoesNotContain("password", parameters.Keys);
    }

    [Fact]
    public void AddUrlAlsoCarriesThePassword()
    {
        var result = new SearchResult { Title = "A Book" };

        var parameters = SabnzbdAddRequestPlanner.BuildQueryParams(
            Client(), result, "https://example.test/a.nzb", "s3cret");

        Assert.Equal("s3cret", parameters["password"]);
        Assert.Equal("A Book", parameters["nzbname"]);
    }
}

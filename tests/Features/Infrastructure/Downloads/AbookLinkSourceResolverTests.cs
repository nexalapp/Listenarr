using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Downloads;

/// <summary>
/// Claiming and ordering for the abook.link submission resolver.
/// </summary>
[Trait("Name", "AbookLinkSourceResolverTests")]
[Trait("Category", "DownloadClientAdapter")]
public sealed class AbookLinkSourceResolverTests : BaseTests
{
    private static AbookLinkSourceResolver Resolver() =>
        new(Mock.Of<IAbookGrabResolver>(),
            Mock.Of<INzbFileDownloader>(),
            Mock.Of<ILogger<AbookLinkSourceResolver>>());

    private static TrustedDownloadCandidate Candidate(string implementation) => new(
        Id: "abook:1",
        Title: "A Book",
        Artist: "An Author",
        Album: "A Book",
        Source: "Abook",
        Quality: "M4B",
        Language: null,
        Size: 1,
        Seeders: 0,
        SourceDescriptor: new DownloadSourceDescriptor(
            Protocol: DownloadProtocol.Usenet,
            Locators: [new DownloadSourceLocator(DownloadSourceLocatorKind.ReleaseId, "1")],
            IndexerId: 1,
            IndexerImplementation: implementation,
            FileName: null));

    [Fact]
    public void ItClaimsAbookLinkResults()
    {
        Assert.True(Resolver().CanResolve(Candidate("AbookLink")));
        Assert.True(Resolver().CanResolve(Candidate("abooklink")));
    }

    [Fact]
    public void ItLeavesOtherSourcesAlone()
    {
        Assert.False(Resolver().CanResolve(Candidate("Newznab")));
        Assert.False(Resolver().CanResolve(Candidate("MyAnonamouse")));
    }

    [Fact]
    public void ItRunsBeforeTheGenericUsenetResolver()
    {
        // Resolvers are tried highest priority first. abook.link results carry a topic
        // reference and no NZB locator, so the generic usenet resolver would reject them
        // outright if it were reached first.
        var generic = new GenericUsenetSourceResolver(Mock.Of<INzbFileDownloader>());

        Assert.True(Resolver().Priority > generic.Priority);
    }
}

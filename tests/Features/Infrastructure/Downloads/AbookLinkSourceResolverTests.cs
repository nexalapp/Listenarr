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

    private static TrustedDownloadCandidate Candidate(
        string implementation,
        string id = "abook:119599",
        string releaseId = "abook:119599") => new(
        Id: id,
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
            Locators: [new DownloadSourceLocator(DownloadSourceLocatorKind.ReleaseId, releaseId)],
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
        Assert.False(Resolver().CanResolve(Candidate("Newznab", id: "abc", releaseId: "abc")));
        Assert.False(Resolver().CanResolve(Candidate("MyAnonamouse", id: "123", releaseId: "123")));
    }

    [Fact]
    public void ItClaimsByIdPrefixWhenTheImplementationNameDidNotSurvive()
    {
        // The implementation name has to round trip through a search response and an
        // encrypted download reference. When it did not, the generic usenet resolver took
        // the candidate and rejected it for having no NZB locator - the failure seen the
        // first time a result was grabbed from the interactive list.
        var candidate = Candidate(implementation: string.Empty);

        Assert.True(Resolver().CanResolve(candidate));
        Assert.Equal(119599, AbookLinkSourceResolver.TryReadTopicId(candidate));
    }

    [Fact]
    public void ThePrefixIsStrippedFromTheTopicId()
    {
        // Results are identified as abook:<topicId>, so parsing the id as a bare number
        // fails - which it did, silently, behind the claiming bug.
        Assert.Equal(119599, AbookLinkSourceResolver.TryReadTopicId(Candidate("AbookLink")));
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

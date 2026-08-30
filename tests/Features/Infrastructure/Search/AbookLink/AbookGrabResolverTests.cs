using Listenarr.Application.Search.AbookLink;
using Listenarr.Application.Search.Nzb;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Search.AbookLink;

/// <summary>
/// The grab's decisions, independent of the network.
///
/// These pin the choices that cost something or mislead somebody: posting a public thanks
/// that was not needed, and collapsing distinct failures into one unhelpful message.
/// </summary>
[Trait("Name", "AbookGrabResolverTests")]
[Trait("Category", "AbookLink")]
public sealed class AbookGrabResolverTests : BaseTests
{
    private const string Identity = "Title: A Book\nAuthor: An Author\n";

    [Fact]
    public void APostThatAlreadyShowsItsPayloadNeedsNoThanks()
    {
        // An account that has thanked a post before still sees the payload. Thanking again
        // posts a second public action for nothing.
        var post = AbookPostParser.Parse(
            $"{Identity}\nHidden content:\nSearch:\nCode:\nTOKEN\n");

        Assert.True(post.CanGrab);
    }

    [Fact]
    public void AGatedPostDoesNotYetYieldASearchString()
    {
        var post = AbookPostParser.Parse(
            $"{Identity}\nYou must thank this post to see the content.\n");

        Assert.False(post.CanGrab);
        Assert.Equal(AbookParseOutcome.MissingSearchString, post.Outcome);
    }

    [Fact]
    public void ARequestTopicIsRefusedRatherThanThanked()
    {
        // Thanking a request wastes a public action and resolves to nothing.
        var post = AbookPostParser.Parse(Identity, "[REQUEST] Someone - A Book (2024)");

        Assert.Equal(AbookParseOutcome.NotARelease, post.Outcome);
    }

    [Fact]
    public void StillPropagatingIsDistinguishedFromNeverIndexed()
    {
        // The remedies differ: one is worth retrying, the other needs an NZB by hand.
        var propagating = new NzbResolution(false, null, null,
        [
            NzbResolverResult.Failed("NZBIndex", NzbResolutionFailure.OnlyIncomplete, "missing parts")
        ]);

        var absent = new NzbResolution(false, null, null,
        [
            NzbResolverResult.Failed("NZBIndex", NzbResolutionFailure.NotIndexed, "nothing")
        ]);

        Assert.True(propagating.WorthRetrying);
        Assert.False(absent.WorthRetrying);
    }

    [Fact]
    public void ASkippedMeteredIndexIsWorthRetrying()
    {
        // "NZBKing was never asked" is not the same as "NZBKing does not have it", and a
        // configured key may yet resolve the release.
        var skipped = new NzbResolution(false, null, null,
        [
            NzbResolverResult.Failed("NZBIndex", NzbResolutionFailure.NotIndexed, "nothing"),
            NzbResolverResult.Failed("NZBKing", NzbResolutionFailure.Unavailable, "no API key configured")
        ]);

        Assert.True(skipped.WorthRetrying);
    }

    [Fact]
    public void EveryIndexAskedIsRecordedSoAStallCanBeExplained()
    {
        var resolution = new NzbResolution(false, null, null,
        [
            NzbResolverResult.Failed("NZBIndex", NzbResolutionFailure.NotIndexed, "nothing"),
            NzbResolverResult.Failed("Binsearch", NzbResolutionFailure.NotIndexed, "nothing"),
            NzbResolverResult.Failed("NZBKing", NzbResolutionFailure.BudgetExhausted, "out of tokens")
        ]);

        Assert.Equal(3, resolution.Attempts.Count);
        Assert.Contains(resolution.Attempts, a => a.Failure == NzbResolutionFailure.BudgetExhausted);
    }

    [Fact]
    public void APasswordIsCarriedAlongsideTheSearchString()
    {
        // Losing it here surfaces much later as an unexplained extraction failure.
        var post = AbookPostParser.Parse(
            $"{Identity}\nHidden content:\nSearch:\nCode:\nTOKEN\nPassword:\nCode:\nSECRET\n");

        Assert.Equal("TOKEN", post.SearchString);
        Assert.Equal("SECRET", post.Password);
    }
}

using Listenarr.Application.Search.AbookLink;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.AbookLink;

/// <summary>
/// Parses a real fuzzy-search response captured from the live site.
/// </summary>
[Trait("Name", "AbookSearchResultParserTests")]
[Trait("Category", "AbookLink")]
public sealed class AbookSearchResultParserTests : BaseTests
{
    [Fact]
    public void ResultsAreReadFromSingleQuotedAnchors()
    {
        // The tool writes href='...' - a parser that only accepts double quotes finds
        // nothing, which is how this first ran against the live site.
        var hits = AbookSearchResultParser.Parse(Fixture());

        Assert.Equal(3, hits.Count);
        Assert.Equal(107230, hits[0].TopicId);
        Assert.Equal("Brandon Sanderson - Mistborn 03 - The Hero of Ages (2008) (64k)", hits[0].Title);
    }

    [Fact]
    public void ThePagesOwnFeedbackLinkIsNotAResult()
    {
        // The header links topic 53798 to submit feedback. Taking every topic link on the
        // page returns it as a release, which is what happened on the first live run.
        var hits = AbookSearchResultParser.Parse(Fixture());

        Assert.DoesNotContain(hits, hit => hit.TopicId == 53798);
    }

    [Fact]
    public void TheReportedTotalIsRead()
    {
        Assert.Equal(41, AbookSearchResultParser.ParseTotalResults(Fixture()));
    }

    [Fact]
    public void ArchivePostsAreStillReturnedSoTheyCanBeClassifiedLater()
    {
        // Filtering belongs to the classifier, which explains its reasons; dropping them
        // here would make them invisible instead.
        var hits = AbookSearchResultParser.Parse(Fixture());

        Assert.Contains(hits, hit => hit.Title.StartsWith("[SPOT]", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyOrMalformedPageYieldsNothing()
    {
        Assert.Empty(AbookSearchResultParser.Parse(null));
        Assert.Empty(AbookSearchResultParser.Parse("<html><body>no results</body></html>"));
    }

    private static string Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Fixtures", "AbookLink")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "Fixtures", "AbookLink", "fuzzy-search-results.html"));
    }
}

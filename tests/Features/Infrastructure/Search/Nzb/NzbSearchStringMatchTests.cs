/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using Listenarr.Application.Search.Nzb;
using Listenarr.Infrastructure.Search.Nzb;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Search.Nzb;

/// <summary>
/// An index hit is only the release when it carries the whole search string. The cases
/// are the two grabs that fetched someone else's book: Solaris and SpecOps.
/// </summary>
[Trait("Name", "NzbSearchStringMatchTests")]
[Trait("Category", "NzbResolvers")]
public sealed class NzbSearchStringMatchTests : BaseTests
{
    private const string Solaris = "abook.link - fm8lssqHWLW7Bownef";

    [Theory]
    // The hit NZBKing put first for Solaris, which was an unrelated 847 MB upload.
    [InlineData(Solaris, "abook.link - 07693b35587d45689f [1/59] \"abook.link - 07693b35587d45689f.par2\" yEnc", false)]
    // The hit it put first for SpecOps: the uploader's prefix and nothing else in common.
    [InlineData("abook - LooquipahZuo4apha6unee7ahf", "abook [01/41] - \"z1f3myd36zqf0wrd7c616l.7z.001\" yEnc", false)]
    [InlineData(Solaris, "abook.link - fm8lssqHWLW7Bownef [1/59] \"abook.link - fm8lssqHWLW7Bownef.par2\" yEnc", true)]
    [InlineData("820c9aa85c1b42f5a07b41af820856d8", "820c9aa85c1b42f5a07b41af820856d8 [1/32] \"820c9aa85c1b42f5a07b41af820856d8.par2\" yEnc", true)]
    // Case and punctuation are not part of the identity; the words are.
    [InlineData("PW - JK-EV03-MPRDWN", "[01/30] - \"pw.jk.ev03.mprdwn.part01.rar\" yEnc", true)]
    // A code is a word, not a substring: a longer code containing it is another upload.
    [InlineData("abook.link - fm8lss", "abook.link - fm8lssqHWLW7Bownef [1/59]", false)]
    public void AHitMustCarryEveryWordOfTheSearchString(string searchString, string subject, bool expected)
    {
        Assert.Equal(expected, NzbSearchStringMatch.Matches(searchString, subject));
    }

    [Fact]
    public void AnEmptySearchStringMatchesNothing()
    {
        Assert.False(NzbSearchStringMatch.Matches("", "anything"));
        Assert.False(NzbSearchStringMatch.Matches(" - ", "anything"));
        Assert.False(NzbSearchStringMatch.Matches(null, "anything"));
    }

    [Fact]
    public void TheLargestHitIsNotTakenWhenItIsAnotherRelease()
    {
        // NZBIndex and Binsearch pick the largest complete hit; that is only safe among
        // hits that are this release.
        var candidates = new List<NzbCandidate>
        {
            new("other", "abook.link - 07693b35587d45689f [1/59]", SizeBytes: 888_000_000, Complete: true),
            new("this", "abook.link - fm8lssqHWLW7Bownef [1/23]", SizeBytes: 200_000_000, Complete: true)
        };

        var result = NzbResolverChain.SelectBest("Index", Solaris, candidates, id => $"https://example.test/{id}");

        Assert.True(result.Succeeded);
        Assert.Equal("https://example.test/this", result.NzbUrl);
    }

    [Fact]
    public void OnlyOtherReleasesIsNotIndexedAndKeepsWhatWasTurnedAway()
    {
        var candidates = new List<NzbCandidate>
        {
            new("other", "abook.link - 07693b35587d45689f [1/59]", SizeBytes: 888_000_000, Complete: true)
        };

        var result = NzbResolverChain.SelectBest("Index", Solaris, candidates, id => $"https://example.test/{id}");

        Assert.False(result.Succeeded);
        Assert.Equal(NzbResolutionFailure.NotIndexed, result.Failure);
        Assert.Contains("none carries the whole search string", result.Detail);
        Assert.Single(result.Candidates!);
    }

    [Fact]
    public void NzbKing_TheRealFeedForSolarisHoldsNoHitForIt()
    {
        var candidates = NzbKingResolver.ParseFeed(Fixture("nzbking-search-feed-other-releases.xml"));

        Assert.NotNull(candidates);
        Assert.Equal(25, candidates!.Count);
        Assert.StartsWith("nzb:", candidates[0].Id);
        Assert.StartsWith("abook.link - 07693b35587d45689f", candidates[0].Subject);
        Assert.Empty(NzbSearchStringMatch.Filter(Solaris, candidates));
    }

    [Fact]
    public void NzbKing_TheRealFeedForAnExactCodeHoldsTheRelease()
    {
        const string brushfire = "820c9aa85c1b42f5a07b41af820856d8";

        var candidates = NzbKingResolver.ParseFeed(Fixture("nzbking-search-feed-exact-hit.xml"));

        var hit = Assert.Single(NzbSearchStringMatch.Filter(brushfire, candidates!));
        Assert.Equal("nzb:601b5e57fbf3ad1c564deda8", hit.Id);
    }

    [Fact]
    public void NzbKing_AnHtmlPageIsNotAFeedThatHoldsNothing()
    {
        // Without a valid key NZBKing answers the feed path with 200 and a web page.
        Assert.Null(NzbKingResolver.ParseFeed("<!DOCTYPE html>\n<html><head></head><body></body></html>"));
        Assert.Null(NzbKingResolver.ParseFeed(""));
        Assert.Null(NzbKingResolver.ParseFeed(null));
        Assert.Empty(NzbKingResolver.ParseFeed("<rss version=\"2.0\"><channel></channel></rss>")!);
    }

    private static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Fixtures", "Resolvers")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "Fixtures", "Resolvers", name));
    }
}

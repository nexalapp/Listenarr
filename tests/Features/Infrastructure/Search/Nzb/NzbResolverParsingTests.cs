using Listenarr.Application.Search.Nzb;
using Listenarr.Infrastructure.Search.Nzb;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Search.Nzb;

/// <summary>
/// Parses the responses actually captured from each index.
/// Fixtures live in tests/Fixtures/Resolvers.
/// </summary>
[Trait("Name", "NzbResolverParsingTests")]
[Trait("Category", "NzbResolvers")]
public sealed class NzbResolverParsingTests : BaseTests
{
    [Fact]
    public void NzbIndex_ReadsARealSearchResponse()
    {
        var candidates = NzbIndexResponseParser.Parse(Fixture("nzbindex-search-response.json"));

        Assert.Equal(2, candidates.Count);

        var first = candidates[0];
        Assert.Equal("16f8540e-192a-318a-9cdd-54b583f7c405", first.Id);
        Assert.Contains("Godfather.Audiobook.Collection", first.Subject);
        Assert.Equal(2726786312L, first.SizeBytes);
        Assert.Equal(28, first.FileCount);
        Assert.False(first.Complete);
        Assert.Equal(["alt.binaries.sounds.music"], first.Groups);
        Assert.Equal("0GUfeAdF07me@2FFFNabO.4SU", first.Poster);
        Assert.NotNull(first.PostedUtc);

        Assert.True(candidates[1].Complete);
    }

    [Fact]
    public void Binsearch_ReadsARealResultRow()
    {
        var candidates = BinsearchResultParser.Parse(Fixture("binsearch-result-row.html"));

        var row = Assert.Single(candidates);

        // The checkbox name is base64 of the NZBIndex id for the same article.
        Assert.Equal("MTZmODU0MGUtMTkyYS0zMThhLTljZGQtNTRiNTgzZjdjNDA1", row.Id);
        Assert.Contains("Godfather.Audiobook.Collection", row.Subject);
        Assert.Equal(28, row.FileCount);
        Assert.False(row.Complete);
        Assert.Equal(["alt.binaries.sounds.music"], row.Groups);
        Assert.Equal("0GUfeAdF07me@2FFFNabO.4SU", row.Poster);
        Assert.NotNull(row.SizeBytes);
    }

    [Fact]
    public void BothIndexesAgreeOnTheArticleId()
    {
        // Binsearch and NZBIndex are one index behind two frontends. If this ever stops
        // holding, using Binsearch to fetch an article found via NZBIndex breaks.
        var fromApi = NzbIndexResponseParser.Parse(Fixture("nzbindex-search-response.json"))[0].Id;
        var fromHtml = BinsearchResultParser.Parse(Fixture("binsearch-result-row.html"))[0].Id;

        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(fromHtml));

        Assert.Equal(fromApi, decoded);
    }

    [Fact]
    public void MalformedResponsesYieldNothingRatherThanThrowing()
    {
        Assert.Empty(NzbIndexResponseParser.Parse("not json at all"));
        Assert.Empty(NzbIndexResponseParser.Parse(null));
        Assert.Empty(NzbIndexResponseParser.Parse("""{"error":true,"errorMessage":"boom"}"""));
        Assert.Empty(BinsearchResultParser.Parse("<html><body>nothing here</body></html>"));
        Assert.Empty(BinsearchResultParser.Parse(null));
    }

    [Fact]
    public void AnEmptyPageIsNotIndexedRatherThanAnError()
    {
        // NZBIndex answers this way when the size parameter is omitted, which must not be
        // mistaken for a malformed response.
        var candidates = NzbIndexResponseParser.Parse(
            """{"data":{"content":[],"page":{"size":0,"number":0,"totalElements":0,"totalPages":1}},"error":false}""");

        Assert.Empty(candidates);
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

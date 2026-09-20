using Listenarr.Application.Search.AbookLink;
using Listenarr.Infrastructure.Search.AbookLink;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Search.AbookLink;

[Trait("Name", "AbookPostHtmlTests")]
[Trait("Category", "AbookLink")]
public sealed class AbookPostHtmlTests : BaseTests
{
    [Fact]
    public void ScriptContentsAreDiscardedBeforeAnythingElse()
    {
        // Inline script assignments look exactly like labelled NFO fields once tags are
        // stripped; a live run reported sScriptUrl and iTopicId as unrecognised labels.
        const string html = """
            <html><head><script>var sScriptUrl = "https://abook.link"; var iTopicId = 42;</script></head>
            <body><div>Title: A Real Book</div></body></html>
            """;

        var text = AbookPostHtml.ToText(html);

        Assert.DoesNotContain("sScriptUrl", text, StringComparison.Ordinal);
        Assert.DoesNotContain("iTopicId", text, StringComparison.Ordinal);
        Assert.Contains("Title: A Real Book", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LineStructureSurvivesSoLabelledFieldsStayOnTheirOwnLines()
    {
        const string html = "<div>Title: A Book<br/>Author: Someone<br/>Read By: A Narrator</div>";

        var lines = AbookPostHtml.ToText(html).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["Title: A Book", "Author: Someone", "Read By: A Narrator"], lines);
    }

    [Fact]
    public void OnlyTheFirstPostIsKept()
    {
        // A reply's text would otherwise be parsed as part of the release, and its own
        // "Title:" line could overwrite the real one.
        const string html = """
            <div class="post" id="msg_100">Title: The Real Book<br/>Author: Real Author</div>
            <div class="post" id="msg_101">Title: A Reply Mentioning Another Book</div>
            """;

        var text = AbookPostHtml.ToText(AbookPostHtml.FirstPost(html));

        Assert.Contains("The Real Book", text, StringComparison.Ordinal);
        Assert.DoesNotContain("A Reply Mentioning", text, StringComparison.Ordinal);
    }

    [Fact]
    public void APageWithNoMessageAnchorIsKeptWhole()
    {
        // Reading too much is recoverable; reading nothing is not.
        const string html = "<div>Title: A Book</div>";

        Assert.Contains("Title: A Book", AbookPostHtml.ToText(AbookPostHtml.FirstPost(html)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheThankedByRollIsCutAway()
    {
        // That list runs to hundreds of usernames on a popular post, and every one of them
        // is noise the parser would otherwise sift through.
        const string html =
            "<div class=\"post\" id=\"msg_100\">Title: The Real Book<br/>"
            + "The following users thanked this post: alice, bob, carol</div>";

        var text = AbookPostHtml.ToText(AbookPostHtml.FirstPost(html));

        Assert.Contains("The Real Book", text, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", text, StringComparison.Ordinal);
    }

    [Fact]
    public void APayloadWrittenWithoutLineBreaksStillParses()
    {
        // Topic 78402 as served: the labels, code headers and code elements follow one
        // another with no <br> or newline between them. Only the tags separate them, so
        // the text conversion has to break lines at opening tags as well as closing ones,
        // or "Search:Code: [Copy]" arrives as one line the payload reader cannot name.
        const string html =
            "<div class=\"post\" id=\"msg_100\"><div class=\"inner\">Title: Fleet of Stars<br/>"
            + "<div class=\"unhiddenbox\"><h6>Hidden content:</h6>Search:"
            + "<div class=\"codeheader\">Code: <a class=\"codeoperation\">[Copy]</a></div>"
            + "<code class=\"bbc_code\">abook.link - SCRUBBEDSEARCH0000000001</code>Password:"
            + "<div class=\"codeheader\">Code: <a class=\"codeoperation\">[Copy]</a></div>"
            + "<code class=\"bbc_code\">Finite.Illusive.Velcro.Enclose</code></div></div></div>";

        var post = AbookPostParser.Parse(AbookPostHtml.ToText(AbookPostHtml.FirstPost(html)));

        Assert.Equal("abook.link - SCRUBBEDSEARCH0000000001", post.SearchString);
        Assert.Equal("Finite.Illusive.Velcro.Enclose", post.Password);
        Assert.True(post.CanGrab);
    }

    [Fact]
    public void EntitiesAreDecoded()
    {
        Assert.Contains("Tom & Jerry", AbookPostHtml.ToText("<div>Author: Tom &amp; Jerry</div>"), StringComparison.Ordinal);
    }
}

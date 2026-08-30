using Listenarr.Application.Search.AbookLink;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.AbookLink;

/// <summary>
/// The payload block is the only part of a post that gates a grab, so its three
/// labellings are pinned directly rather than only through whole-post fixtures.
/// </summary>
[Trait("Name", "AbookPayloadParsingTests")]
[Trait("Category", "AbookLink")]
public sealed class AbookPayloadParsingTests : BaseTests
{
    private const string Identity = "Title: A Book\nAuthor: An Author\n";

    [Theory]
    [InlineData("Search:")]
    [InlineData("Search for:")]
    public void RecognisedLabels_AreConsumedAsLabelsNotProse(string label)
    {
        var post = AbookPostParser.Parse($"{Identity}\nHidden content:\n{label}\nCode:\nTHETOKEN\n");

        Assert.Equal("THETOKEN", post.SearchString);

        // A label left in the notes shows up as noise in the UI, and means the labelling
        // was not understood — the failure mode this test exists to catch.
        Assert.DoesNotContain("Search", post.PayloadNotes ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnlabelledCode_IsTakenAsTheSearchString()
    {
        // Archive posts and some older ones lead straight into a code with no label.
        var post = AbookPostParser.Parse($"{Identity}\nHidden content:\nCode:\nBARETOKEN\n");

        Assert.Equal("BARETOKEN", post.SearchString);
        Assert.Null(post.Password);
    }

    [Fact]
    public void PasswordFollowsItsOwnLabel()
    {
        var post = AbookPostParser.Parse(
            $"{Identity}\nHidden content:\nSearch:\nCode:\nTHETOKEN\nPassword:\nCode:\nSECRET\n");

        Assert.Equal("THETOKEN", post.SearchString);
        Assert.Equal("SECRET", post.Password);
    }

    [Fact]
    public void PasswordIsNeverMistakenForTheSearchString()
    {
        // Sending a password where a search string belongs resolves nothing and leaks the
        // password into a third-party query, so the ordering must hold.
        var post = AbookPostParser.Parse(
            $"{Identity}\nHidden content:\nPassword:\nCode:\nSECRET\nSearch:\nCode:\nTHETOKEN\n");

        Assert.Equal("THETOKEN", post.SearchString);
        Assert.Equal("SECRET", post.Password);
    }

    [Fact]
    public void TrailingProseIsKeptButNotTreatedAsAValue()
    {
        var post = AbookPostParser.Parse(
            $"{Identity}\nHidden content:\nSearch:\nCode:\nTHETOKEN\nin a.b.misc\n");

        Assert.Equal("THETOKEN", post.SearchString);
        Assert.Equal("a.b.misc", post.NewsgroupHint);
        Assert.Contains("a.b.misc", post.PayloadNotes);
    }

    [Fact]
    public void AMultiPartReleaseIsFlagged()
    {
        var post = AbookPostParser.Parse(
            $"{Identity}\nHidden content:\nSearch for:\nCode:\nabook.link - TOKEN Some Book\n" +
            "Note: My posts don't show up as one collection in nzbindex.nl\n" +
            "Recommend you search for string above then hit the\n" +
            "\"Select All\" button and then the \"Create NZB\" button\n");

        Assert.True(post.MultiPart);
        Assert.Equal("abook.link - TOKEN Some Book", post.SearchString);
    }

    [Fact]
    public void TheCopyButtonLabelIsNeverMistakenForTheValue()
    {
        // The forum renders the copy button beside the label, so stripping markup puts
        // "Code: [Copy]" on one line. Taking the rest of that line resolved to an
        // unrelated release instead of failing visibly - the worst kind of wrong.
        var sameLine = AbookPostParser.Parse(
            $"{Identity}\nHidden content:\nSearch:\nCode: [Copy]\nTHETOKEN\n");

        Assert.Equal("THETOKEN", sameLine.SearchString);

        var trailing = AbookPostParser.Parse(
            $"{Identity}\nHidden content:\nSearch:\nCode:\n[Copy]\nTHETOKEN\n");

        Assert.Equal("THETOKEN", trailing.SearchString);
    }

    [Fact]
    public void ACopyLabelBesideThePasswordIsAlsoStripped()
    {
        var post = AbookPostParser.Parse(
            $"{Identity}\nHidden content:\nSearch:\nCode: [Copy]\nTHETOKEN\nPassword:\nCode: [Copy]\nSECRET\n");

        Assert.Equal("THETOKEN", post.SearchString);
        Assert.Equal("SECRET", post.Password);
    }

    [Fact]
    public void NoHiddenBlockLeavesTheGrabBlockedRatherThanGuessing()
    {
        var post = AbookPostParser.Parse($"{Identity}\nYou must thank this post to see the content.\n");

        Assert.Null(post.SearchString);
        Assert.Equal(AbookParseOutcome.MissingSearchString, post.Outcome);
    }
}

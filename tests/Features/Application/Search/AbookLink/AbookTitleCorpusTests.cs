using Listenarr.Application.Search.AbookLink;
using Listenarr.Tests.Common;
using Xunit.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.AbookLink;

/// <summary>
/// Scores title parsing over real abook.link topic titles.
///
/// This is the measurement harness, distinct from the fixture suite. The fixtures are a
/// regression net built from posts the parser was written against; scoring against those
/// would only measure memorisation. This runs over titles captured in bulk from board
/// listings, and reports how often a usable author and title come out.
///
/// The floor is asserted so a regression fails the build; the printed report is what
/// says which titles to look at next.
/// </summary>
[Trait("Name", "AbookTitleCorpusTests")]
[Trait("Category", "AbookLink")]
public sealed class AbookTitleCorpusTests : BaseTests
{
    private const double RequiredSuccessRate = 0.80;

    private readonly ITestOutputHelper _output;

    public AbookTitleCorpusTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TitleParsing_MeetsTheRequiredSuccessRate()
    {
        var titles = LoadCorpus();
        Assert.NotEmpty(titles);

        var releases = new List<string>();
        var identified = new List<string>();
        var missed = new List<string>();

        foreach (var title in titles)
        {
            var parts = AbookTopicTitle.Parse(title);

            // Requests, filled requests and reading orders are correctly-classified
            // skips, not parse failures.
            if (parts.IsNotARelease)
            {
                continue;
            }

            releases.Add(title);

            if (parts.Author is { Length: > 0 } && parts.Title is { Length: > 0 })
            {
                identified.Add(title);
            }
            else
            {
                missed.Add(title);
            }
        }

        var rate = (double)identified.Count / releases.Count;

        _output.WriteLine($"Corpus: {titles.Count} titles, {releases.Count} releases");
        _output.WriteLine($"Identified author + title: {identified.Count}/{releases.Count} = {rate:P1}");
        _output.WriteLine($"Floor: {RequiredSuccessRate:P0}");

        if (missed.Count > 0)
        {
            _output.WriteLine("");
            _output.WriteLine("Not identified — these are the next parser improvements:");
            foreach (var title in missed)
            {
                _output.WriteLine($"  {title}");
            }
        }

        Assert.True(
            rate >= RequiredSuccessRate,
            $"Title identification fell to {rate:P1}, below the {RequiredSuccessRate:P0} floor. "
            + $"Unidentified: {string.Join(" | ", missed)}");
    }

    [Fact]
    public void NonReleaseTitles_AreAllRecognisedAsSuch()
    {
        // Offering a request or a reading order as though it were a grabbable release is
        // worse than missing one: it wastes a thanks and resolves to nothing.
        var missed = LoadCorpus()
            .Where(t => t.StartsWith("[REQUEST]", StringComparison.OrdinalIgnoreCase)
                     || t.StartsWith("[FILLED]", StringComparison.OrdinalIgnoreCase)
                     || t.StartsWith("[Reading Order]", StringComparison.OrdinalIgnoreCase))
            .Where(t => !AbookTopicTitle.Parse(t).IsNotARelease)
            .ToList();

        Assert.True(missed.Count == 0, $"Not recognised as non-releases: {string.Join(" | ", missed)}");
    }

    [Fact]
    public void ArchiveTitles_AreAllFlagged()
    {
        var missed = LoadCorpus()
            .Where(t => t.StartsWith("[SPOT]", StringComparison.OrdinalIgnoreCase))
            .Where(t => !AbookTopicTitle.Parse(t).IsArchiveSpot)
            .ToList();

        Assert.True(missed.Count == 0, $"Not flagged as archive: {string.Join(" | ", missed)}");
    }

    private static List<string> LoadCorpus()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Fixtures", "AbookLink")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return File.ReadAllLines(Path.Combine(dir!.FullName, "Fixtures", "AbookLink", "topic-titles.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
    }
}

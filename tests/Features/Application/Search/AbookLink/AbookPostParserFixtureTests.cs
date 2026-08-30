using Listenarr.Application.Search.AbookLink;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.AbookLink;

/// <summary>
/// Runs the parser over every captured post shape.
///
/// abook.link posts are hand-written and the sampled ones disagree on field names, units,
/// which sections exist and how the payload is labelled. Each fixture is a shape seen in
/// the wild; adding a newly-discovered shape is one file plus one row here, and every
/// shape we have ever seen stays covered.
///
/// Corpus and findings: tests/Fixtures/AbookLink/README.md
/// </summary>
[Trait("Name", "AbookPostParserFixtureTests")]
[Trait("Category", "AbookLink")]
public sealed class AbookPostParserFixtureTests : BaseTests
{
    public sealed record Expected(
        string Fixture,
        AbookParseOutcome Outcome,
        string? Title = null,
        string? Author = null,
        string? Narrator = null,
        string? SeriesName = null,
        string? SeriesPosition = null,
        int? Year = null,
        string? Format = null,
        int? FileCount = null,
        int? DurationSeconds = null,
        long? SizeBytes = null,
        string? Asin = null,
        string? SearchString = null,
        string? Password = null,
        string? NewsgroupHint = null,
        bool? MultiPart = null,
        string? CompressedWith = null,
        bool? Abridged = null);

    public static TheoryData<Expected> Corpus() =>
    [
        new Expected("degaussed-mistborn-mp3.txt", AbookParseOutcome.Complete,
            Title: "The Hero of Ages", Author: "Brandon Sanderson", Narrator: "Michael Kramer",
            SeriesName: "Mistborn", SeriesPosition: "03", Year: 2008,
            Format: "MP3", FileCount: 82, DurationSeconds: 98625,
            SearchString: "SCRUBBEDSEARCH0000000001", Password: null),

        new Expected("stalkerama-misfit-m4b.txt", AbookParseOutcome.Complete,
            Title: "Misfit Alpha", Author: "Stephanie Foxe", Narrator: "Amanda Dolan",
            SeriesName: "An Urban Fantasy (Cursed World)", SeriesPosition: "5", Year: 2021,
            Format: "m4b", FileCount: 1, DurationSeconds: 49920, Asin: "B098836DDB",
            SearchString: "SCRUBBEDSEARCH0000000002", Password: "SCRUBBEDPASSWORD000000002"),

        new Expected("arif-taxman-no-fileinfo.txt", AbookParseOutcome.Complete,
            Title: "T-Rexes and Tax Law", Author: "Rachel Ford", Narrator: "John Carter Aimone",
            SeriesName: "Time Traveling Taxman", SeriesPosition: "1", Year: 2019,
            DurationSeconds: 22500, Abridged: false,
            SearchString: "00000000000000000000000000000003", NewsgroupHint: "a.b.misc"),

        new Expected("3josh-czarzakian-media-info.txt", AbookParseOutcome.Complete,
            Author: "Bruce X. Brown", Narrator: "Rhett Samuel Price", Year: 2019,
            DurationSeconds: 32126, MultiPart: true),

        new Expected("chev-labyrinth-filetype.txt", AbookParseOutcome.MissingSearchString,
            Title: "Labyrinth", Author: "A.G. Riddle", Narrator: "James Babson", Year: 2025,
            Format: "M4B | NMR", FileCount: 1, DurationSeconds: 77013),

        new Expected("zaster379-resonance-combined-series.txt", AbookParseOutcome.Complete,
            Title: "Theater of War", Author: "Aaron Renfroe", Narrator: "Christian J. Gilliland",
            SeriesName: "The Resonance Cycle", SeriesPosition: "02", Year: 2023,
            FileCount: 49, DurationSeconds: 45720, CompressedWith: "Winrar", Abridged: false,
            SearchString: "SCRUBBEDSEARCH0000000009", Password: "ScrubbedWord9"),

        new Expected("postbot-spot-archive.txt", AbookParseOutcome.ArchiveSpot),
        new Expected("postbot-spot-archive-with-payload.txt", AbookParseOutcome.ArchiveSpot),
    ];

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Parse_MatchesTheCapturedShape(Expected expected)
    {
        var (topicTitle, body) = LoadFixture(expected.Fixture);

        var post = AbookPostParser.Parse(body, topicTitle);

        Assert.Equal(expected.Outcome, post.Outcome);

        Check(expected.Title, post.Title, nameof(expected.Title));
        Check(expected.Author, post.Author, nameof(expected.Author));
        Check(expected.Narrator, post.Narrator, nameof(expected.Narrator));
        Check(expected.SeriesName, post.SeriesName, nameof(expected.SeriesName));
        Check(expected.SeriesPosition, post.SeriesPosition, nameof(expected.SeriesPosition));
        Check(expected.Format, post.Format, nameof(expected.Format));
        Check(expected.Asin, post.Asin, nameof(expected.Asin));
        Check(expected.SearchString, post.SearchString, nameof(expected.SearchString));
        Check(expected.Password, post.Password, nameof(expected.Password));
        Check(expected.NewsgroupHint, post.NewsgroupHint, nameof(expected.NewsgroupHint));
        Check(expected.CompressedWith, post.CompressedWith, nameof(expected.CompressedWith));

        if (expected.Year is { } year) Assert.Equal(year, post.Year);
        if (expected.FileCount is { } files) Assert.Equal(files, post.FileCount);
        if (expected.MultiPart is { } multi) Assert.Equal(multi, post.MultiPart);
        if (expected.Abridged is { } abridged) Assert.Equal(abridged, post.Abridged);
        // Expressed in seconds deliberately: TimeSpan.Parse("27:23:45") means 27 *days*,
        // which is exactly the trap an audiobook duration walks into.
        if (expected.DurationSeconds is { } seconds)
        {
            Assert.Equal(TimeSpan.FromSeconds(seconds), post.Duration);
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Parse_NeverInventsAPayload(Expected expected)
    {
        var (topicTitle, body) = LoadFixture(expected.Fixture);

        var post = AbookPostParser.Parse(body, topicTitle);

        // A search string the post does not contain is worse than none: it resolves to
        // the wrong release rather than failing visibly.
        if (post.SearchString is { Length: > 0 })
        {
            Assert.Contains(post.SearchString, body, StringComparison.Ordinal);
        }

        if (post.Password is { Length: > 0 })
        {
            Assert.Contains(post.Password, body, StringComparison.Ordinal);
        }
    }

    private static void Check(string? expected, string? actual, string field)
    {
        if (expected is not null)
        {
            Assert.True(expected == actual, $"{field}: expected \"{expected}\" but got \"{actual}\"");
        }
    }

    private static (string TopicTitle, string Body) LoadFixture(string name)
    {
        var path = Path.Combine(FixtureRoot(), name);
        var lines = File.ReadAllLines(path);

        var title = lines.FirstOrDefault(l => l.StartsWith("Topic:", StringComparison.Ordinal))?["Topic:".Length..].Trim()
            ?? string.Empty;

        var firstBlank = Array.FindIndex(lines, string.IsNullOrWhiteSpace);
        var body = firstBlank >= 0 ? string.Join('\n', lines[(firstBlank + 1)..]) : string.Join('\n', lines);

        return (title, body);
    }

    private static string FixtureRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Fixtures", "AbookLink")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Fixtures", "AbookLink");
    }
}

using Listenarr.Application.Search.NzbKing;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.NzbKing;

/// <summary>
/// Reading the configured keys, and migrating the single-key property installs upgraded
/// in place still hold.
/// </summary>
[Trait("Name", "NzbKingSettingsTests")]
[Trait("Category", "NzbKingSettings")]
public sealed class NzbKingSettingsTests : BaseTests
{
    [Fact]
    public void EveryKeyInTheArrayIsRead()
    {
        var keys = NzbKingSettings.TryGetApiKeys("""{"nzbking_api_keys":["one","two","three"]}""");

        Assert.Equal(["one", "two", "three"], keys);
    }

    [Fact]
    public void TheSingleKeyPropertyIsStillRead()
    {
        // An install upgraded in place holds only this until the source is next saved.
        // Ignoring it would spend a fresh allowance the installation does not have.
        var keys = NzbKingSettings.TryGetApiKeys("""{"nzbking_api_key":"legacy"}""");

        Assert.Equal(["legacy"], keys);
    }

    [Fact]
    public void BothPropertiesAreReadAndDuplicatesCollapse()
    {
        var keys = NzbKingSettings.TryGetApiKeys(
            """{"nzbking_api_keys":["one","legacy"],"nzbking_api_key":"legacy"}""");

        Assert.Equal(["one", "legacy"], keys);
    }

    [Fact]
    public void BlankAndNonStringEntriesAreIgnored()
    {
        var keys = NzbKingSettings.TryGetApiKeys(
            """{"nzbking_api_keys":["one","  ","",null,7,"  two  "]}""");

        Assert.Equal(["one", "two"], keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void NothingConfiguredReadsAsNoKeys(string? settings)
    {
        Assert.Empty(NzbKingSettings.TryGetApiKeys(settings));
    }

    [Fact]
    public void NormaliseMovesTheSingleKeyIntoTheArrayAndRemovesIt()
    {
        // Both sides of the edit round-trip normalise, so they agree on the shape. A form
        // shown an array while the database still held a single property would send back a
        // placeholder that matched nothing, and the key would be lost.
        var normalised = NzbKingSettings.Normalise(
            """{"abook_username":"nexal","nzbking_api_key":"legacy"}""");

        Assert.Equal(["legacy"], NzbKingSettings.TryGetApiKeys(normalised));
        Assert.DoesNotContain("nzbking_api_key\"", normalised, StringComparison.Ordinal);
        Assert.Contains("nexal", normalised, StringComparison.Ordinal);
    }

    [Fact]
    public void NormaliseAppendsToAnArrayThatAlreadyExists()
    {
        var normalised = NzbKingSettings.Normalise(
            """{"nzbking_api_keys":["one"],"nzbking_api_key":"legacy"}""");

        Assert.Equal(["one", "legacy"], NzbKingSettings.TryGetApiKeys(normalised));
    }

    [Fact]
    public void NormaliseDoesNotDuplicateAKeyHeldInBothPlaces()
    {
        var normalised = NzbKingSettings.Normalise(
            """{"nzbking_api_keys":["legacy"],"nzbking_api_key":"legacy"}""");

        Assert.Equal(["legacy"], NzbKingSettings.TryGetApiKeys(normalised));
    }

    [Fact]
    public void NormaliseDropsAnEmptySingleKeyWithoutAddingIt()
    {
        var normalised = NzbKingSettings.Normalise(
            """{"nzbking_api_keys":["one"],"nzbking_api_key":""}""");

        Assert.Equal(["one"], NzbKingSettings.TryGetApiKeys(normalised));
        Assert.DoesNotContain("nzbking_api_key\"", normalised, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"abook_username":"nexal"}""")]
    [InlineData("""{"nzbking_api_keys":["one"]}""")]
    public void NormaliseLeavesABlobWithNothingToMigrateExactlyAsItArrived(string? settings)
    {
        // Returned byte for byte rather than re-serialised: this runs on every read of
        // every indexer and has no business rewriting settings it does not own.
        Assert.Equal(settings, NzbKingSettings.Normalise(settings));
    }

    [Fact]
    public void NormaliseSurvivesASingleKeyThatIsNotAString()
    {
        // Whatever is in the database is what arrives here, and throwing from the middle
        // of a save over a malformed value would take the whole indexer with it.
        var normalised = NzbKingSettings.Normalise("""{"nzbking_api_key":7,"abook_username":"nexal"}""");

        Assert.Empty(NzbKingSettings.TryGetApiKeys(normalised));
        Assert.Contains("nexal", normalised, StringComparison.Ordinal);
    }
}

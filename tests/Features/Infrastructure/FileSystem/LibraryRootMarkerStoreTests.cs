using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.FileSystem;

[Trait("Name", "LibraryRootMarkerStoreTests")]
[Trait("Category", "Infrastructure")]
public sealed class LibraryRootMarkerStoreTests : BaseTests
{
    [Fact]
    public void Enroll_WritesAMarkerTheStoreThenRecognises()
    {
        using var root = new TemporaryDirectory();
        var store = new LibraryRootMarkerStore();

        var markerId = store.Enroll(root.Path);

        Assert.NotEqual(Guid.Empty, markerId);
        Assert.Equal(
            LibraryRootMarkerState.Matched,
            store.Read(root.Path, markerId).State);
    }

    [Fact]
    public void Enroll_AdoptsAnExistingMarker_SoReconfirmationIsIdempotent()
    {
        using var root = new TemporaryDirectory();
        var store = new LibraryRootMarkerStore();

        var first = store.Enroll(root.Path);
        var second = store.Enroll(root.Path);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Read_WithoutAMarker_ReportsMissing()
    {
        using var root = new TemporaryDirectory();
        var store = new LibraryRootMarkerStore();

        var reading = store.Read(root.Path, Guid.NewGuid());

        Assert.Equal(LibraryRootMarkerState.Missing, reading.State);
    }

    [Fact]
    public void Read_WithAnotherLibrarysMarker_ReportsMismatched()
    {
        using var root = new TemporaryDirectory();
        var store = new LibraryRootMarkerStore();
        store.Enroll(root.Path);

        var reading = store.Read(root.Path, Guid.NewGuid());

        Assert.Equal(LibraryRootMarkerState.Mismatched, reading.State);
    }

    [Fact]
    public void Read_SurvivesTheDirectoryItselfBeingReplaced()
    {
        // The whole point of the marker: identity travels with the content, not with
        // the inode. Recreating the directory and moving the marker back into it is
        // what a remount looks like from the application's side.
        using var root = new TemporaryDirectory();
        var store = new LibraryRootMarkerStore();
        var markerId = store.Enroll(root.Path);
        var markerPath = Path.Combine(root.Path, "listenarr-library.id");
        var stashed = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        File.Move(markerPath, stashed);
        Directory.Delete(root.Path, recursive: true);
        Directory.CreateDirectory(root.Path);
        File.Move(stashed, markerPath);

        Assert.Equal(
            LibraryRootMarkerState.Matched,
            store.Read(root.Path, markerId).State);
    }

    [Fact]
    public void Read_OfAMalformedMarker_ReportsUnreadableRatherThanMismatched()
    {
        // An unreadable marker must not be mistaken for a foreign library: the caller
        // falls back to the native identity instead of accusing the operator of a swap.
        using var root = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(root.Path, "listenarr-library.id"),
            "this is not a library marker");
        var store = new LibraryRootMarkerStore();

        var reading = store.Read(root.Path, Guid.NewGuid());

        Assert.Equal(LibraryRootMarkerState.Unreadable, reading.State);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "listenarr-marker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // A leaked temp directory must never fail a test run.
            }
        }
    }
}

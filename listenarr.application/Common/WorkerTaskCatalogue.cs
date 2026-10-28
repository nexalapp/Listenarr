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
using System.Text.RegularExpressions;

namespace Listenarr.Application.Common
{
    /// <summary>What a worker is called on the tasks page, and what it does.</summary>
    public sealed record WorkerTaskDescription(string Label, string Description);

    /// <summary>
    /// Plain names for the background workers.
    ///
    /// A worker registers itself by class name, which is the right key and the wrong
    /// label: "SuggestionReleaseCheckBackgroundService" says nothing to someone trying
    /// to work out why their books are not appearing. The description is what the tasks
    /// page shows behind the information icon, and is written for whoever is reading the
    /// page rather than whoever wrote the class.
    ///
    /// A worker not listed here still appears, under a name derived from its class, so
    /// adding a worker never hides it - it only leaves it less well explained.
    /// </summary>
    public static partial class WorkerTaskCatalogue
    {
        private static readonly Dictionary<string, WorkerTaskDescription> Known =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["AuthorMonitoringBackgroundService"] = new(
                    "Check monitored authors",
                    "Looks through the authors you follow for books the library does not have, "
                    + "and adds them when you have asked for that."),
                ["AutoGrabBackgroundService"] = new(
                    "Auto-grab from suggestions",
                    "Adds books from the suggestion lists you have ticked and sends them to your "
                    + "download client. It adds at most the configured number per run, however "
                    + "many are waiting."),
                ["AutomaticSearchService"] = new(
                    "Search for monitored books",
                    "Looks for a release for every monitored book that still has no file."),
                ["conversion"] = new(
                    "Convert books to M4B",
                    "Works through the conversion queue, turning books made of many MP3s into a "
                    + "single M4B with chapters, cover art and a description."),
                ["DirectDownloadService"] = new(
                    "Fetch direct downloads",
                    "Downloads releases that arrive as a plain file rather than through a "
                    + "download client."),
                ["DiscoverBackgroundService"] = new(
                    "Refresh Discover and Newly posted",
                    "Reads another page of the Audible charts and the abook.link boards you "
                    + "follow, a page at a time so neither site is hammered."),
                ["DownloadMonitorService"] = new(
                    "Watch the download client",
                    "Asks the download client how each download is getting on, and notices when "
                    + "one finishes or fails."),
                ["DownloadProcessingJobCleanupService"] = new(
                    "Tidy finished download jobs",
                    "Removes the bookkeeping left behind by downloads that finished long ago."),
                ["FoundBookScanBackgroundService"] = new(
                    "Scan for books found on disk",
                    "Looks through your root folders for audiobooks that are not in the library "
                    + "yet, so they can be matched and imported."),
                ["ImageCacheCleanupService"] = new(
                    "Tidy the cover image cache",
                    "Deletes cached cover art that nothing refers to any more."),
                ["MetadataRescanService"] = new(
                    "Refresh book details",
                    "Re-reads titles, narrators, series and descriptions from Audible for books "
                    + "whose information has gone stale."),
                ["move.scan.handoff.recovery"] = new(
                    "Recover interrupted moves",
                    "Finishes any file move that a restart or a crash cut short, so no book is "
                    + "left half-moved."),
                ["MovedDownloadCleanupService"] = new(
                    "Tidy moved downloads",
                    "Clears away downloads whose files have already been moved into the library."),
                ["NzbKingKeepaliveBackgroundService"] = new(
                    "Keep NZBKing keys alive",
                    "Touches each NZBKing key occasionally so it is not deleted for inactivity. "
                    + "Costs one token per key."),
                ["quality-sweep"] = new(
                    "Check audio quality",
                    "Listens to a little of each book to work out its real bitrate and spot "
                    + "recordings that are poorer than their file claims."),
                ["ReleasePreparationBackgroundService"] = new(
                    "Prepare releases ahead of time",
                    "For a book already known to be available, works out in advance exactly what "
                    + "would be downloaded, so that pressing Add only has to send it."),
                ["repair"] = new(
                    "Repair audio and chapters",
                    "Works through the repair queue, cleaning up hiss and bandwidth problems and "
                    + "rebuilding chapter marks."),
                ["repair-2"] = new(
                    "Repair audio (second lane)",
                    "A second book from the repair queue, so one can be encoding while the next "
                    + "is being cleaned up."),
                ["SeriesMonitoringBackgroundService"] = new(
                    "Check monitored series",
                    "Looks through the series you follow for books the library does not have."),
                ["SuggestionCatalogBackgroundService"] = new(
                    "Refresh suggestions",
                    "Rebuilds the suggestion lists from the authors and series in your library."),
                ["SuggestionReleaseCheckBackgroundService"] = new(
                    "Check what is available",
                    "Asks your indexers whether each suggested or wanted book can be found "
                    + "anywhere, one book at a time. This is what decides whether a book shows "
                    + "as available or as nobody having it."),
                ["tagging"] = new(
                    "Write tags to files",
                    "Works through the tagging queue, writing titles, series, narrators and "
                    + "descriptions into the audio files themselves."),
            };

        public static WorkerTaskDescription Describe(string workerName) =>
            Known.TryGetValue(workerName, out var known)
                ? known
                : new WorkerTaskDescription(Humanise(workerName), string.Empty);

        /// <summary>
        /// A readable name for a worker nobody has written a description for:
        /// "ThingDoerBackgroundService" becomes "Thing doer".
        /// </summary>
        internal static string Humanise(string workerName)
        {
            var trimmed = Suffixes().Replace(workerName ?? string.Empty, string.Empty);
            var spaced = CamelBoundary().Replace(trimmed.Replace('.', ' ').Replace('-', ' '), " $1");
            var words = spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return workerName ?? string.Empty;
            }

            return string.Join(
                ' ',
                words.Select((word, index) => index == 0
                    ? char.ToUpperInvariant(word[0]) + word[1..]
                    : word.ToLowerInvariant()));
        }

        [GeneratedRegex(@"(BackgroundService|Service)$", RegexOptions.IgnoreCase)]
        private static partial Regex Suffixes();

        [GeneratedRegex(@"(?<=[a-z0-9])([A-Z])")]
        private static partial Regex CamelBoundary();
    }
}

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
namespace Listenarr.Application.Common
{
    /// <summary>
    /// The value renderers every naming and tag builder shares: how an author, a
    /// narrator list and a series name are written. Public so RenameService and the
    /// tag planners agree with the path patterns, and synchronous because they run
    /// inside pattern rendering - settings come from the snapshot.
    /// </summary>
    public partial class FileNamingService
    {
        /// <summary>
        /// The series name as it is written: with the configured trailing words dropped.
        /// Read from the settings snapshot because the renderers are synchronous; with no
        /// snapshot (tests, or before the first settings load) the defaults apply.
        /// </summary>
        public string RenderAuthor(string? series, IEnumerable<string>? authors)
        {
            var primary = authors?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))?.Trim()
                ?? "Unknown Author";
            return FilingAuthor(series, primary);
        }

        public string RenderNarrators(string? narrators) =>
            NarratorNameStyle.Render(
                narrators,
                _settingsSnapshot?.Current?.MaxNarratorsInNames ?? 0);

        public string RenderSeriesName(string? name) =>
            SeriesNameStyle.Render(
                name,
                _settingsSnapshot?.Current == null
                    ? SeriesNameStyle.DefaultDropWords
                    : SeriesNameStyle.ParseDropWords(_settingsSnapshot.Current.SeriesNameDropWordsJson));

        /// <summary>
        /// The author a book files under: its series' first author when the setting says
        /// so and the series is known, otherwise its own primary author.
        /// </summary>
        private string FilingAuthor(AudioMetadata metadata) =>
            FilingAuthor(metadata.Series, PrimaryAuthor(metadata));

        private string FilingAuthor(string? series, string primaryAuthor)
        {
            var bySeries = _settingsSnapshot?.Current?.FileSeriesUnderFirstAuthor ?? true;
            if (bySeries && _seriesAuthors != null && !string.IsNullOrWhiteSpace(series))
            {
                var seriesAuthor = _seriesAuthors.AuthorFor(series);
                if (!string.IsNullOrWhiteSpace(seriesAuthor))
                {
                    return seriesAuthor;
                }
            }

            return primaryAuthor;
        }

        private static string PrimaryAuthor(AudioMetadata metadata)
        {
            if (metadata.Authors is { Count: > 0 })
            {
                return metadata.PrimaryAuthor;
            }

            // No list, only a joined string: the first name in it.
            var chosen = ChooseAuthor(metadata);
            var comma = chosen.IndexOf(", ", StringComparison.Ordinal);
            return comma > 0 ? chosen[..comma] : chosen;
        }
    }
}

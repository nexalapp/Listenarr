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
namespace Listenarr.Domain.Common
{
    /// <summary>
    /// What to announce an audio file as when handing its bytes to a browser.
    /// </summary>
    /// <remarks>
    /// A browser decides how to decode from this header alone, never from the extension,
    /// so an <c>.m4b</c> has to be announced as the MP4 container it is rather than as a
    /// type of its own. Shared rather than restated per endpoint because every endpoint
    /// that streams a library file has to make the same claim about it: two tables would
    /// eventually disagree, and the one that was wrong would simply fail to play.
    /// </remarks>
    public static class AudioContentTypes
    {
        private static readonly IReadOnlyDictionary<string, string> ByExtension =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".m4b"] = "audio/mp4",
                [".m4a"] = "audio/mp4",
                [".alac"] = "audio/mp4",
                [".aac"] = "audio/aac",
                [".mp3"] = "audio/mpeg",
                [".flac"] = "audio/flac",
                [".ogg"] = "audio/ogg",
                [".opus"] = "audio/ogg",
                [".wav"] = "audio/wav",
                [".aif"] = "audio/aiff",
                [".aiff"] = "audio/aiff",
                [".wma"] = "audio/x-ms-wma",
                [".wv"] = "audio/x-wavpack",
                [".ape"] = "audio/x-monkeys-audio",
            };

        /// <summary>
        /// The content type for this file, or <c>application/octet-stream</c> when the
        /// extension is one nothing here claims to know how to announce.
        /// </summary>
        public static string ForFile(string? path) =>
            path != null && ByExtension.TryGetValue(Path.GetExtension(path), out var mapped)
                ? mapped
                : "application/octet-stream";
    }
}

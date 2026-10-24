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
namespace Listenarr.Domain.Audiobooks.Quality
{
    /// <summary>
    /// What the files looked like when they were probed, so a verdict is trusted only
    /// while they are unchanged — the same bargain the audit strikes with its transcript.
    /// Format: <c>"3600s|3610s~q1"</c> — per-file whole-second durations, then the probe
    /// version, which changes whenever the measures or thresholds do and so re-opens
    /// every stored verdict at once.
    /// </summary>
    public static class AudioQualityIdentity
    {
        /// <summary>Bumped when the probe's measures change meaning, never for a refactor.</summary>
        public const int ProbeVersion = 2;

        public static string Of(IEnumerable<double> fileDurationsSeconds)
        {
            var parts = fileDurationsSeconds
                .Select(d => $"{Math.Round(d):0}s")
                .ToList();
            return $"{string.Join("|", parts)}~q{ProbeVersion}";
        }

        /// <summary>The suffix every identity of the current probe version carries.</summary>
        public static string CurrentVersionSuffix => $"~q{ProbeVersion}";

        /// <summary>Whether a stored verdict still describes these files under this probe.</summary>
        public static bool Matches(string? stored, IEnumerable<double> fileDurationsSeconds) =>
            !string.IsNullOrWhiteSpace(stored) && stored == Of(fileDurationsSeconds);
    }
}

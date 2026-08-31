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
using System.Globalization;
using System.Text.RegularExpressions;

namespace Listenarr.Application.Search.AbookLink
{
    /// <summary>
    /// Normalises the values abook.link posters write by hand.
    ///
    /// Every method returns null rather than a default when it cannot be sure. A wrong
    /// duration or size is worse than an absent one: absent shows as a dash, wrong shows
    /// as fact and can silently fail a quality profile.
    /// </summary>
    public static partial class AbookValues
    {
        [GeneratedRegex(@"^\s*(\d{1,3}):([0-5]?\d):([0-5]?\d)\s*$")]
        private static partial Regex ClockDuration();

        [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(hours|hour|hrs|hr|h|minutes|minute|mins|min|m|seconds|second|secs|sec|s)\b",
            RegexOptions.IgnoreCase)]
        private static partial Regex UnitDuration();

        [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(tb|gb|mb|kb|b)?\b", RegexOptions.IgnoreCase)]
        private static partial Regex SizeValue();

        [GeneratedRegex(@"\b(19|20)\d{2}\b")]
        private static partial Regex YearValue();

        [GeneratedRegex(@"/pd/([A-Z0-9]{10})\b", RegexOptions.IgnoreCase)]
        private static partial Regex AudibleAsin();

        [GeneratedRegex(@"^(.*?)[\s,]*(?:book\s*)?(\d{1,3}(?:\.\d)?)\s*$", RegexOptions.IgnoreCase)]
        private static partial Regex TrailingPosition();

        /// <summary>
        /// Parses the five duration spellings seen in the wild:
        /// <c>27:23:45</c>, <c>13h 52m</c>, <c>6 hrs 15 mins</c>,
        /// <c>8 hours, 55 minutes, 26 seconds</c>, <c>12hr 42min</c>.
        /// </summary>
        public static TimeSpan? ParseDuration(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var clock = ClockDuration().Match(value);
            if (clock.Success)
            {
                return new TimeSpan(
                    int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture),
                    int.Parse(clock.Groups[3].Value, CultureInfo.InvariantCulture));
            }

            double hours = 0, minutes = 0, seconds = 0;
            var matched = false;

            foreach (Match part in UnitDuration().Matches(value))
            {
                if (!double.TryParse(part.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
                {
                    continue;
                }

                var unit = part.Groups[2].Value.ToLowerInvariant();
                matched = true;

                if (unit.StartsWith('h')) hours += amount;
                else if (unit.StartsWith('m')) minutes += amount;
                else seconds += amount;
            }

            if (!matched)
            {
                return null;
            }

            return TimeSpan.FromSeconds((hours * 3600) + (minutes * 60) + seconds);
        }

        /// <summary>
        /// Parses a size. A bare number with no unit (seen as <c>756.47</c>) is read as
        /// megabytes, which is the only reading consistent with the posts that do state a
        /// unit — but it is a guess, and callers that care should check
        /// <paramref name="unitWasStated"/>.
        /// </summary>
        public static long? ParseSize(string? value, out bool unitWasStated)
        {
            unitWasStated = false;
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var match = SizeValue().Match(value);
            if (!match.Success)
            {
                return null;
            }

            var numeric = match.Groups[1].Value.Replace(',', '.');
            if (!double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            {
                return null;
            }

            var unit = match.Groups[2].Success ? match.Groups[2].Value.ToLowerInvariant() : null;
            unitWasStated = unit is { Length: > 0 };

            var multiplier = unit switch
            {
                "tb" => 1024L * 1024 * 1024 * 1024,
                "gb" => 1024L * 1024 * 1024,
                "kb" => 1024L,
                "b" => 1L,
                _ => 1024L * 1024
            };

            return (long)(amount * multiplier);
        }

        /// <summary>First four-digit year in the text, or null.</summary>
        public static int? ParseYear(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var match = YearValue().Match(value);
            return match.Success && int.TryParse(match.Value, CultureInfo.InvariantCulture, out var year)
                ? year
                : null;
        }

        /// <summary>Extracts an ASIN from an Audible product link.</summary>
        public static string? ParseAsin(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var match = AudibleAsin().Match(value);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }

        /// <summary>
        /// Normalises a series position. Posters write <c>03</c>, <c>5</c> or <c>Book 1</c>;
        /// the leading word is dropped and the number kept as written, since <c>03</c> and
        /// <c>3</c> both appear and the original is what a human recognises.
        /// </summary>
        public static string? ParsePosition(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            var match = TrailingPosition().Match(trimmed);
            return match.Success ? match.Groups[2].Value : trimmed;
        }

        /// <summary>
        /// Splits a combined <c>Series &amp; Position</c> value such as
        /// <c>The Resonance Cycle 02</c> into its name and its trailing position.
        /// </summary>
        public static (string? Name, string? Position) SplitSeriesAndPosition(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return (null, null);
            }

            var trimmed = value.Trim();
            var match = TrailingPosition().Match(trimmed);
            if (!match.Success)
            {
                return (trimmed, null);
            }

            var name = match.Groups[1].Value.Trim();
            return name.Length == 0 ? (trimmed, null) : (name, match.Groups[2].Value);
        }
    }
}

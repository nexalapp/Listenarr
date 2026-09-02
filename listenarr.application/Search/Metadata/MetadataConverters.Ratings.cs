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

namespace Listenarr.Application.Search.Metadata;

/// <summary>
/// Listener ratings, which the two providers do not publish the same way.
///
/// <para>
/// Audible scores three things independently — the book overall, the narration and the
/// writing — each at full precision and with the number of ratings behind it. Audnexus
/// republishes Audible's overall average alone, rounded to one decimal, as a string, with
/// no count. They therefore land in separate columns: merged, a stored rating's precision
/// and its provenance would depend on which provider happened to answer that day.
/// </para>
/// </summary>
public partial class MetadataConverters
{
    /// <summary>
    /// Copies Audible's ratings across, and carries an Audnexus rating through when this
    /// response is an Audnexus lookup wearing the Audible shape (see
    /// <c>AudiobookMetadataService</c>). A real Audible response leaves that one null.
    /// </summary>
    /// <remarks>
    /// Averages are copied at the precision Audible reports rather than rounded here, so
    /// the rounding stays a display decision — 4.87 overall against 4.93 for the narration
    /// is the distinction the split exists to preserve, and it does not survive one decimal
    /// place.
    /// </remarks>
    private static void ApplyRatings(AudibleBookMetadata metadata, AudibleBookResponse response)
    {
        metadata.AudibleRatingOverall = response.Rating?.Overall?.AverageRating;
        metadata.AudibleRatingOverallCount = response.Rating?.Overall?.NumRatings;
        metadata.AudibleRatingPerformance = response.Rating?.Performance?.AverageRating;
        metadata.AudibleRatingPerformanceCount = response.Rating?.Performance?.NumRatings;
        metadata.AudibleRatingStory = response.Rating?.Story?.AverageRating;
        metadata.AudibleRatingStoryCount = response.Rating?.Story?.NumRatings;

        // Written reviews, a far smaller population than the star ratings counted above --
        // 47,698 against 310,988 on B08G9PRS1K. Kept apart from them because the two are
        // easy to confuse and differ by an order of magnitude.
        metadata.AudibleReviewCount = response.Rating?.NumReviews;

        metadata.AudnexusRating = response.AudnexusRating;
    }

    /// <summary>
    /// Audnexus' rating, which arrives as a string such as "4.9".
    /// </summary>
    /// <remarks>
    /// Parsed invariantly: the value always uses '.' as its decimal separator, so parsing
    /// under the server's culture would read "4.9" as 49 wherever '.' is the group
    /// separator — the same trap <see cref="Audiobook.CreateBasicAudioMetadata"/> documents
    /// for series positions.
    /// <para>
    /// A value outside 0–5 is discarded rather than clamped. Audnexus scrapes its ratings,
    /// and a number off that scale means the scrape returned something that is not a
    /// rating; clamping would launder that into a plausible-looking 5.
    /// </para>
    /// </remarks>
    internal static double? ParseAudnexusRating(string? rating)
    {
        if (string.IsNullOrWhiteSpace(rating) ||
            !double.TryParse(rating.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        // Audnexus reports 0 for a book nobody has rated, which is an absence rather than a
        // score of zero.
        return parsed is > 0 and <= 5 ? parsed : null;
    }
}

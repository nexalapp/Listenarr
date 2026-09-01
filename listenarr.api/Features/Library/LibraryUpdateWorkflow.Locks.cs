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

namespace Listenarr.Api.Features.Library;

public sealed partial class LibraryUpdateWorkflow
{
    /// <summary>
    /// The book's lock set after this update.
    /// </summary>
    /// <remarks>
    /// Two rules, and which applies depends on whether the caller said anything about locks.
    /// <list type="bullet">
    /// <item>
    /// <c>LockedFields</c> sent — it is the answer, exactly. The edit form shows a padlock
    /// per field and lights one the moment its value changes, so the set it submits is the
    /// set the operator was looking at. Adding a lock the padlocks did not show would make
    /// the screen a lie, and it would make unlocking a field you are also correcting
    /// impossible: the auto-lock would put it straight back.
    /// </item>
    /// <item>
    /// <c>LockedFields</c> omitted — the stored locks are kept, and every field this
    /// request <em>changes</em> is locked on top. A caller that knows nothing about locks
    /// still gets the protection, which is the point: correcting a value by hand and
    /// watching a rescan undo it is the problem, and remembering a second step afterwards
    /// is exactly what gets missed.
    /// </item>
    /// </list>
    /// <para>
    /// Called before the request's values are assigned, because "changed" is a comparison
    /// against what is still stored.
    /// </para>
    /// </remarks>
    /// <param name="existing">The book as stored, before this request is applied.</param>
    /// <param name="request">The update being applied.</param>
    /// <param name="suppressStaleImageUrl">
    /// When the cover URL in the request is being ignored as stale. A field that is not
    /// going to be written must not be locked on the strength of the value that was
    /// discarded.
    /// </param>
    internal static List<string> ResolveLockedFields(
        Audiobook existing,
        AudiobookUpdateRequest request,
        bool suppressStaleImageUrl)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(request);

        if (request.LockedFields != null)
        {
            return LockableFields.Normalize(request.LockedFields);
        }

        var resolved = LockableFields.AsSet(existing.LockedFields)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var field in ChangedLockableFields(existing, request, suppressStaleImageUrl))
        {
            resolved.Add(field);
        }

        return LockableFields.Normalize(resolved);
    }

    /// <summary>
    /// Which lockable fields this request would actually change.
    /// </summary>
    /// <remarks>
    /// A comparison rather than a presence check. The edit form already sends only what
    /// changed, but the endpoint is public, and a client that PUTs the whole record would
    /// otherwise lock every field on a save that changed nothing.
    /// </remarks>
    private static IEnumerable<string> ChangedLockableFields(
        Audiobook existing,
        AudiobookUpdateRequest request,
        bool suppressStaleImageUrl)
    {
        if (Differs(request.Title, existing.Title)) yield return LockableFields.Title;
        if (Differs(request.Subtitle, existing.Subtitle)) yield return LockableFields.Subtitle;
        if (Differs(request.Description, existing.Description)) yield return LockableFields.Description;
        if (Differs(request.Publisher, existing.Publisher)) yield return LockableFields.Publisher;
        if (Differs(request.Language, existing.Language)) yield return LockableFields.Language;
        if (Differs(request.PublishYear, existing.PublishYear)) yield return LockableFields.PublishYear;
        if (Differs(request.PublishedDate, existing.PublishedDate)) yield return LockableFields.PublishedDate;

        if (Differs(request.Authors, existing.Authors)) yield return LockableFields.Authors;
        if (Differs(request.Narrators, existing.Narrators)) yield return LockableFields.Narrators;
        if (Differs(request.Genres, existing.Genres)) yield return LockableFields.Genres;

        if (request.Runtime.HasValue && request.Runtime != existing.Runtime)
        {
            yield return LockableFields.Runtime;
        }

        if (!suppressStaleImageUrl && Differs(request.ImageUrl, existing.ImageUrl))
        {
            yield return LockableFields.Cover;
        }

        if (SeriesChanged(existing, request))
        {
            yield return LockableFields.Series;
        }
    }

    /// <summary>
    /// Whether the request's series differs from the stored one.
    ///
    /// Compared on the memberships an operator can actually edit — name, position and which
    /// is primary — rather than on database ids, so reordering the same two series counts
    /// and a save that re-sends them unchanged does not.
    /// </summary>
    private static bool SeriesChanged(Audiobook existing, AudiobookUpdateRequest request)
    {
        if (request.SeriesMemberships == null)
        {
            // Books predating memberships carry their series in the legacy pair alone.
            return Differs(request.Series, existing.Series)
                || Differs(request.SeriesNumber, existing.SeriesNumber);
        }

        static string Describe(IEnumerable<AudiobookSeriesMembership>? memberships) =>
            string.Join(
                "",
                (memberships ?? [])
                    .Where(membership => !string.IsNullOrWhiteSpace(membership.SeriesName))
                    .OrderByDescending(membership => membership.IsPrimary)
                    .ThenBy(membership => membership.SortOrder)
                    .Select(membership => string.Join(
                        "",
                        membership.SeriesName?.Trim() ?? string.Empty,
                        membership.SeriesNumber?.Trim() ?? string.Empty,
                        membership.IsPrimary ? "1" : "0")));

        return !string.Equals(
            Describe(request.SeriesMemberships),
            Describe(existing.SeriesMemberships),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a submitted value differs from the stored one. A null request value is an
    /// omitted field, not a request to clear.
    /// </summary>
    private static bool Differs(string? submitted, string? stored) =>
        submitted != null
        && !string.Equals(submitted.Trim(), (stored ?? string.Empty).Trim(), StringComparison.Ordinal);

    private static bool Differs(List<string>? submitted, List<string>? stored)
    {
        if (submitted == null)
        {
            return false;
        }

        static IEnumerable<string> Clean(IEnumerable<string>? values) =>
            (values ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim());

        return !Clean(submitted).SequenceEqual(Clean(stored), StringComparer.Ordinal);
    }
}

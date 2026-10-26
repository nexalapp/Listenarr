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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Listenarr.Application.Search.AbookLink;
using Listenarr.Application.Search.Nzb;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Suggestions
{
    /// <summary>
    /// Does everything a download needs except sending it.
    ///
    /// <para>
    /// A found release is only half an answer: for an abook.link post the bytes are
    /// still behind a thanks, a payload and an index lookup, and all of that used to
    /// happen while somebody waited with a button pressed. Doing it when the book is
    /// found instead turns the press into a send.
    /// </para>
    /// <para>
    /// Posters edit a payload in place when they replace a release, so a stored NZB can
    /// quietly point at a superseded upload. What is stored is therefore a fingerprint
    /// of the payload it was resolved from; the grab re-reads the post — one free
    /// request — and uses the stored NZB only while that still matches.
    /// </para>
    /// </summary>
    public interface IReleasePreparationService
    {
        /// <summary>
        /// Prepares one found release that has not been prepared yet, newest first.
        /// Returns what it did, or null when there was nothing to do.
        /// </summary>
        Task<string?> PrepareNextAsync(CancellationToken cancellationToken = default);

        /// <summary>Prepares one named release, whatever its state. Used by a retry.</summary>
        Task<ReleasePreparationState> PrepareAsync(string asin, CancellationToken cancellationToken = default);
    }

    public sealed class ReleasePreparationService(
        ISuggestionReleaseRepository store,
        IAbookGrabResolver grabs,
        IConfigurationService configuration,
        ISecretProtector secrets,
        TimeProvider timeProvider,
        ILogger<ReleasePreparationService> logger) : IReleasePreparationService
    {
        public async Task<string?> PrepareNextAsync(CancellationToken cancellationToken = default)
        {
            var settings = await configuration.GetApplicationSettingsAsync();
            if (!settings.PrepareReleasesEnabled)
            {
                return null;
            }

            var pending = (await store.GetAllAsync(cancellationToken)).Values
                .Where(release => release.Status == SuggestionReleaseStatus.Found
                    && release.Preparation == ReleasePreparationState.NotPrepared)
                .OrderByDescending(release => release.CheckedAt)
                .FirstOrDefault();

            if (pending is null)
            {
                return null;
            }

            var state = await PrepareAsync(pending.Asin, cancellationToken);
            return $"{pending.ReleaseTitle ?? pending.Asin}: {state}";
        }

        public async Task<ReleasePreparationState> PrepareAsync(
            string asin,
            CancellationToken cancellationToken = default)
        {
            var release = await store.GetAsync(asin, cancellationToken);
            if (release is not { Status: SuggestionReleaseStatus.Found, ProtectedResult: { Length: > 0 } protectedResult })
            {
                return ReleasePreparationState.NotPrepared;
            }

            var settings = await configuration.GetApplicationSettingsAsync();
            var result = JsonSerializer.Deserialize<IndexerSearchResult>(secrets.Unprotect(protectedResult));
            if (result is null)
            {
                return await SaveAsync(release, ReleasePreparationState.Unresolvable,
                    "The stored release could not be read.", null, null, null, cancellationToken);
            }

            // A Newznab hit already carries its NZB link: there is nothing to prepare
            // beyond noticing that, which is most of the work saved for nothing.
            if (!string.IsNullOrWhiteSpace(result.NzbUrl))
            {
                return await SaveAsync(release, ReleasePreparationState.Ready,
                    "The indexer's own NZB link.", result.NzbUrl, result.Source, null, cancellationToken);
            }

            if (!int.TryParse(result.DownloadReference, out var topicId))
            {
                return await SaveAsync(release, ReleasePreparationState.Unresolvable,
                    "This release has neither an NZB link nor a forum topic to resolve.",
                    null, null, null, cancellationToken);
            }

            var grab = await grabs.ResolveAsync(
                topicId, settings.PrepareReleasesMayUseMeteredIndexes, cancellationToken);

            if (grab.Succeeded && grab.NzbUrl is { Length: > 0 })
            {
                return await SaveAsync(release, ReleasePreparationState.Ready,
                    grab.Detail, grab.NzbUrl, grab.Resolution?.ResolvedBy,
                    Fingerprint(grab.Post), cancellationToken);
            }

            // "The free indexes had nothing and we chose not to ask the metered one" is
            // a different answer from "nothing holds this", and only the first is worth
            // revisiting when the user grabs by hand.
            var notAsked = grab.Resolution?.Attempts
                .Any(attempt => attempt.Failure == NzbResolutionFailure.NotAsked) == true;

            return await SaveAsync(
                release,
                notAsked ? ReleasePreparationState.NeedsMeteredIndex : ReleasePreparationState.Unresolvable,
                grab.Detail,
                null,
                null,
                Fingerprint(grab.Post),
                cancellationToken);
        }

        /// <summary>
        /// What the post's payload was when it resolved. A hash rather than the payload
        /// itself: this is the gated value, and the database has no business holding it
        /// twice.
        /// </summary>
        public static string? Fingerprint(AbookPost? post)
        {
            if (post?.SearchString is not { Length: > 0 } search)
            {
                return null;
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{search}\n{post.Password}"));
            return Convert.ToHexString(bytes)[..32];
        }

        private async Task<ReleasePreparationState> SaveAsync(
            SuggestionRelease release,
            ReleasePreparationState state,
            string? detail,
            string? nzbUrl,
            string? resolvedVia,
            string? fingerprint,
            CancellationToken cancellationToken)
        {
            release.Preparation = state;
            release.PreparationDetail = detail;
            release.ProtectedNzbUrl = nzbUrl is { Length: > 0 } url ? secrets.Protect(url) : null;
            release.ResolvedVia = resolvedVia;
            release.PayloadFingerprint = fingerprint;
            release.PreparedAt = timeProvider.GetUtcNow().UtcDateTime;

            await store.SaveAsync(release, cancellationToken);
            logger.LogDebug(
                "Prepared {Asin}: {State} ({Detail})", release.Asin, state, detail);
            return state;
        }
    }
}

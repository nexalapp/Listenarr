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
using Listenarr.Application.Common;
using Listenarr.Application.Common.Exceptions;
using Listenarr.Application.Search.AbookLink;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.AbookLink
{
    public partial class AbookLinkController
    {
        /// <summary>
        /// Sends one abook.link post as the download for a library book.
        ///
        /// For books Audible has no record of: the page adds the book from the post's own
        /// title and author, then sends this post for it, so the download imports into that
        /// book like any grab. The post is read again here rather than trusted from the
        /// page, and it is resolved, thanked and handed to the client by the same path an
        /// abook.link search result takes.
        /// </summary>
        [HttpPost("topic/{topicId:int}/send")]
        public async Task<ActionResult<object>> SendTopic(
            int topicId,
            [FromBody] AbookSendTopicRequest request,
            [FromServices] IDownloadService downloads,
            [FromServices] IIndexerRepository indexers,
            CancellationToken cancellationToken = default)
        {
            if (request is null || request.AudiobookId <= 0)
            {
                return BadRequest(new { message = "Choose the library book this post is for." });
            }

            var source = (await indexers.GetEnabledAsync(isAutomaticSearch: false, cancellationToken))
                .FirstOrDefault(indexer => string.Equals(indexer.Implementation, "AbookLink", StringComparison.OrdinalIgnoreCase));
            if (source is null)
            {
                return Conflict(new { message = "abook.link is not connected. Add it as an indexer first." });
            }

            var topic = await _browser.GetTopicAsync(topicId, cancellationToken);
            if (!topic.Succeeded)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = topic.Reason ?? "abook.link could not be read." });
            }

            var candidate = topic.Candidates.FirstOrDefault();
            if (candidate is null)
            {
                return NotFound(new { message = "That topic was not found." });
            }

            if (candidate.Post.Outcome is AbookParseOutcome.NotARelease or AbookParseOutcome.ArchiveSpot)
            {
                return BadRequest(new { message = "That topic is not a release that can be downloaded." });
            }

            var post = candidate.Post;
            var releaseId = $"abook:{topicId}";
            var trusted = new TrustedDownloadCandidate(
                releaseId,
                ReleaseTitle(post, candidate.TopicTitle),
                post.Author ?? "Unknown",
                post.Title ?? candidate.TopicTitle,
                source.Name,
                post.Format,
                null,
                post.SizeBytes ?? 0,
                null,
                new DownloadSourceDescriptor(
                    source.Id,
                    source.Implementation,
                    DownloadProtocol.Usenet,
                    [new DownloadSourceLocator(DownloadSourceLocatorKind.ReleaseId, releaseId)]));

            try
            {
                var downloadId = await downloads.SendToDownloadClientAsync(trusted, null, request.AudiobookId);
                if (string.IsNullOrEmpty(downloadId))
                {
                    return Conflict(new { message = "That book already has an active download." });
                }

                _logger.LogInformation(
                    "Sent abook.link topic {TopicId} for audiobook {AudiobookId} as download {DownloadId}",
                    topicId, request.AudiobookId, downloadId);
                return Ok(new { downloadId });
            }
            catch (ApplicationConflictException ex)
            {
                return Conflict(new { message = ex.SafeDetail });
            }
            catch (DownloadClientSubmissionException ex)
            {
                // The grab's own words: no index holds the release, the post would not
                // reveal its payload, and so on - each wants a different remedy.
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }
        }

        private static string ReleaseTitle(AbookPost post, string topicTitle)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(post.Author))
            {
                parts.Add(post.Author);
            }

            if (!string.IsNullOrWhiteSpace(post.SeriesName))
            {
                parts.Add($"{post.SeriesName} {post.SeriesPosition}".Trim());
            }

            parts.Add(post.Title ?? topicTitle);
            var title = string.Join(" - ", parts);
            return post.Year is { } year ? $"{title} ({year})" : title;
        }
    }
}

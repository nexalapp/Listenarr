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
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.AbookLink
{
    public sealed class AbookCandidateResponse
    {
        public int TopicId { get; set; }
        public string TopicTitle { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Narrator { get; set; }
        public string? SeriesName { get; set; }
        public string? SeriesPosition { get; set; }
        public int? Year { get; set; }
        public string? Asin { get; set; }
        public string? Format { get; set; }
        public int? FileCount { get; set; }
        public long? SizeBytes { get; set; }
        public string? Duration { get; set; }
        public bool HasPayload { get; set; }
        public bool MultiPart { get; set; }
        public List<string> UnrecognisedLabels { get; set; } = new();
    }

    public sealed class AbookResolverAttemptResponse
    {
        public string Resolver { get; set; } = string.Empty;
        public bool Succeeded { get; set; }
        public string? Failure { get; set; }
        public string? Detail { get; set; }
        public int CandidateCount { get; set; }
    }

    public sealed class AbookGrabResponse
    {
        public int TopicId { get; set; }
        public bool Succeeded { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public bool Thanked { get; set; }
        public bool WorthRetrying { get; set; }
        public string? NzbUrl { get; set; }
        public bool HasPassword { get; set; }
        public AbookCandidateResponse? Book { get; set; }
        public List<AbookResolverAttemptResponse> Attempts { get; set; } = new();
    }

    public sealed class AbookSearchResponse
    {
        public string Query { get; set; } = string.Empty;
        public int HitCount { get; set; }
        public int Inspected { get; set; }
        public double SuccessRate { get; set; }
        public double IdentificationRate { get; set; }
        public string Report { get; set; } = string.Empty;
        public List<AbookCandidateResponse> Candidates { get; set; } = new();
    }

    /// <summary>
    /// A read-only window onto abook.link, for exercising the search and parse path end to
    /// end before any of it is wired into grabbing.
    ///
    /// Everything here is free: searching and reading topics costs no "thanks" and no
    /// NZBKing tokens, so it can be run as often as needed. Nothing on this controller
    /// reveals a payload or downloads anything.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/abook")]
    [Tags("abook.link")]
    public class AbookLinkController : ControllerBase
    {
        private const int DefaultInspect = 10;

        private readonly IAbookLinkBrowser _browser;
        private readonly IAbookGrabResolver _grabs;
        private readonly ILogger<AbookLinkController> _logger;

        public AbookLinkController(
            IAbookLinkBrowser browser,
            IAbookGrabResolver grabs,
            ILogger<AbookLinkController> logger)
        {
            _browser = browser;
            _grabs = grabs;
            _logger = logger;
        }

        /// <summary>
        /// Searches abook.link and parses the topics found, reporting how many were fully
        /// understood and which labels were not recognised.
        /// </summary>
        [HttpGet("search")]
        [ProducesResponseType(typeof(AbookSearchResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<AbookSearchResponse>> Search(
            [FromQuery] string q,
            [FromQuery] int inspect = DefaultInspect,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return BadRequest("A search term is required.");
            }

            var result = await _browser.SearchAsync(q, Math.Clamp(inspect, 1, 25), cancellationToken);
            if (!result.Succeeded)
            {
                return StatusCode(StatusCodes.Status502BadGateway, result.Reason);
            }

            return Ok(Map(q, result));
        }

        /// <summary>
        /// Reports what abook.link replies to a sign-in. For diagnosing a login that does
        /// not take; returns only the forum's own answer, never the credentials.
        /// </summary>
        [HttpGet("diagnose-login")]
        [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyDictionary<string, string>>> DiagnoseLogin(
            CancellationToken cancellationToken = default)
        {
            return Ok(await _browser.DiagnoseLoginAsync(cancellationToken));
        }

        /// <summary>
        /// Resolves a topic to a downloadable NZB.
        ///
        /// This posts a "thanks" to abook.link under the configured account, visibly and
        /// on purpose — it is the only way the payload is revealed. It is a POST rather
        /// than a GET for that reason: nothing here should happen by following a link.
        /// The NZB is not sent to a download client; that is a separate step.
        /// </summary>
        [HttpPost("resolve/{topicId:int}")]
        [ProducesResponseType(typeof(AbookGrabResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<AbookGrabResponse>> Resolve(
            int topicId,
            CancellationToken cancellationToken = default)
        {
            var result = await _grabs.ResolveAsync(topicId, cancellationToken);

            _logger.LogInformation(
                "abook.link grab for topic {TopicId} finished at stage {Stage} (succeeded: {Succeeded})",
                topicId, result.Stage, result.Succeeded);

            return Ok(new AbookGrabResponse
            {
                TopicId = result.TopicId,
                Succeeded = result.Succeeded,
                Stage = result.Stage,
                Detail = result.Detail,
                Thanked = result.Thanked,
                WorthRetrying = result.Resolution?.WorthRetrying ?? false,
                NzbUrl = result.NzbUrl,
                // A flag, not the value: a password belongs in the download client, not in
                // an API response somebody may paste into a bug report.
                HasPassword = result.Password is { Length: > 0 },
                Book = result.Post is null ? null : Map(new AbookCandidate(topicId, string.Empty, result.Post)),
                Attempts = result.Resolution?.Attempts.Select(attempt => new AbookResolverAttemptResponse
                {
                    Resolver = attempt.Resolver,
                    Succeeded = attempt.Succeeded,
                    Failure = attempt.Failure?.ToString(),
                    Detail = attempt.Detail,
                    CandidateCount = attempt.Candidates?.Count ?? 0
                }).ToList() ?? []
            });
        }

        /// <summary>Parses one topic without revealing its payload.</summary>
        [HttpGet("topic/{topicId:int}")]
        [ProducesResponseType(typeof(AbookCandidateResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<AbookCandidateResponse>> Topic(
            int topicId,
            CancellationToken cancellationToken = default)
        {
            var result = await _browser.GetTopicAsync(topicId, cancellationToken);
            if (!result.Succeeded)
            {
                return StatusCode(StatusCodes.Status502BadGateway, result.Reason);
            }

            var candidate = result.Candidates.FirstOrDefault();
            return candidate is null ? NotFound() : Ok(Map(candidate));
        }

        private static AbookSearchResponse Map(string query, AbookBrowseResult result) => new()
        {
            Query = query,
            HitCount = result.HitCount,
            Inspected = result.Candidates.Count,
            SuccessRate = result.Report.SuccessRate,
            IdentificationRate = result.Report.IdentificationRate,
            Report = result.Report.Summarise(),
            Candidates = result.Candidates.Select(Map).ToList()
        };

        private static AbookCandidateResponse Map(AbookCandidate candidate) => new()
        {
            TopicId = candidate.TopicId,
            TopicTitle = candidate.TopicTitle,
            Outcome = candidate.Post.Outcome.ToString(),
            Title = candidate.Post.Title,
            Author = candidate.Post.Author,
            Narrator = candidate.Post.Narrator,
            SeriesName = candidate.Post.SeriesName,
            SeriesPosition = candidate.Post.SeriesPosition,
            Year = candidate.Post.Year,
            Asin = candidate.Post.Asin,
            Format = candidate.Post.Format,
            FileCount = candidate.Post.FileCount,
            SizeBytes = candidate.Post.SizeBytes,
            Duration = candidate.Post.Duration?.ToString(),
            // Deliberately a flag, not the value: this endpoint never reveals a payload.
            HasPayload = candidate.Post.CanGrab,
            MultiPart = candidate.Post.MultiPart,
            UnrecognisedLabels = candidate.Post.UnrecognisedLabels
        };
    }
}

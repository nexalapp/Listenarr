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
using Listenarr.Application.Search.NzbKing;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.AbookLink
{
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
    public partial class AbookLinkController : ControllerBase
    {
        private const int DefaultInspect = 10;

        /// <summary>How far back the ledger is read for display and tallies.</summary>
        private const int RecentAccessLimit = 100;

        private readonly IAbookLinkBrowser _browser;
        private readonly IAbookGrabResolver _grabs;
        private readonly IAbookGrabDispatcher _dispatcher;
        private readonly ILogger<AbookLinkController> _logger;

        public AbookLinkController(
            IAbookLinkBrowser browser,
            IAbookGrabResolver grabs,
            IAbookGrabDispatcher dispatcher,
            ILogger<AbookLinkController> logger)
        {
            _browser = browser;
            _grabs = grabs;
            _dispatcher = dispatcher;
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
            var result = await _grabs.ResolveAsync(topicId, meteredAllowed: true, cancellationToken);

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

        /// <summary>
        /// Resolves a topic and queues it on a download client.
        ///
        /// Posts a thanks to abook.link and starts a download, so it is deliberately a
        /// separate call from resolve: seeing what a grab would do should not require
        /// doing it.
        /// </summary>
        [HttpPost("grab/{topicId:int}")]
        [ProducesResponseType(typeof(AbookSendResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<AbookSendResponse>> Grab(
            int topicId,
            CancellationToken cancellationToken = default)
        {
            var result = await _dispatcher.GrabAsync(topicId, cancellationToken);
            var grab = result.Grab;

            _logger.LogInformation(
                "abook.link grab for topic {TopicId}: stage {Stage}, sent {Sent}",
                topicId, grab.Stage, result.Sent);

            return Ok(new AbookSendResponse
            {
                TopicId = grab.TopicId,
                Succeeded = grab.Succeeded && result.Sent,
                Stage = result.Sent ? "Queued" : grab.Stage,
                Detail = result.SendDetail ?? grab.Detail,
                Thanked = grab.Thanked,
                Sent = result.Sent,
                ClientName = result.ClientName,
                DownloadId = result.DownloadId,
                PasswordSent = result.PasswordSent,
                SendDetail = result.SendDetail,
                Book = grab.Post is null ? null : Map(new AbookCandidate(topicId, string.Empty, grab.Post)),
                Attempts = grab.Resolution?.Attempts.Select(attempt => new AbookResolverAttemptResponse
                {
                    Resolver = attempt.Resolver,
                    Succeeded = attempt.Succeeded,
                    Failure = attempt.Failure?.ToString(),
                    Detail = attempt.Detail,
                    CandidateCount = attempt.Candidates?.Count ?? 0
                }).ToList() ?? []
            });
        }

        /// <summary>
        /// How much of the NZBKing allowance is left.
        ///
        /// The balance is an estimate: NZBKing exposes no way to read the real figure, so
        /// this is what Listenarr has counted. It can drift, which is why spending stops
        /// short of empty rather than at it.
        /// </summary>
        [HttpGet("nzbking-status")]
        [ProducesResponseType(typeof(NzbKingStatusResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<NzbKingStatusResponse>> NzbKingStatus(
            CancellationToken cancellationToken = default)
        {
            var keys = await _browser.GetNzbKingPoolStatusAsync(cancellationToken);

            if (keys.Count == 0)
            {
                return Ok(new NzbKingStatusResponse
                {
                    Configured = false,
                    MaxTokens = NzbKingTokenPolicy.MaxTokens,
                    ReserveFloor = NzbKingTokenPolicy.ReserveFloor,
                    Summary = "No NZBKing key is configured."
                });
            }

            // Per key, because that is how NZBKing meters: a key at its reserve cannot be
            // helped by another key's balance, so the spendable total is the sum of what
            // each key can spend rather than the pool's balance less one reserve.
            var perKey = keys
                .Select(key => new NzbKingKeyStatusResponse
                {
                    Label = Label(key.KeyFingerprint),
                    EstimatedBalance = key.EstimatedBalance,
                    Spendable = key.KeyDeleted
                        ? 0
                        : Math.Max(0, key.EstimatedBalance - NzbKingTokenPolicy.ReserveFloor),
                    NextRefillAt = key.NextRefillAt,
                    LastSuccessfulUseAt = key.LastSuccessfulUseAt,
                    KeyDeleted = key.KeyDeleted
                })
                .ToList();

            var deletedKeys = perKey.Count(key => key.KeyDeleted);
            var liveKeys = perKey.Where(key => !key.KeyDeleted).ToList();
            var balance = liveKeys.Sum(key => key.EstimatedBalance);
            var spendable = liveKeys.Sum(key => key.Spendable);
            var allDeleted = liveKeys.Count == 0;

            // Only the keys that can still earn a token: a deleted key's anchor keeps
            // advancing and would otherwise promise a refill that is never coming.
            var nextRefill = liveKeys
                .Where(key => key.EstimatedBalance < NzbKingTokenPolicy.MaxTokens)
                .Select(key => (DateTime?)key.NextRefillAt)
                .DefaultIfEmpty(null)
                .Min();

            // A day's worth of attempts is the useful window: refills are hourly, so
            // anything older has already been earned back.
            var since = DateTime.UtcNow.AddDays(-1);
            var recent = await _browser.GetNzbKingLedgerAsync(RecentAccessLimit, cancellationToken);
            var refusedRecently = recent.Count(a =>
                a.AttemptedAt >= since && a.Outcome == NzbKingAccessOutcome.DeniedByBudget);

            return Ok(new NzbKingStatusResponse
            {
                Configured = true,
                RefusedRecently = refusedRecently,
                EstimatedBalance = balance,
                MaxTokens = NzbKingTokenPolicy.MaxTokens,
                ReserveFloor = NzbKingTokenPolicy.ReserveFloor,
                Spendable = spendable,
                KeyCount = perKey.Count,
                DeletedKeyCount = deletedKeys,
                Keys = perKey,
                NextRefillAt = nextRefill,
                LastSuccessfulUseAt = perKey
                    .Select(key => key.LastSuccessfulUseAt)
                    .Where(at => at.HasValue)
                    .DefaultIfEmpty(null)
                    .Max(),
                KeyDeleted = allDeleted,
                Summary = Summarise(perKey.Count, liveKeys.Count, deletedKeys, balance, spendable, nextRefill)
            });
        }

        /// <summary>
        /// The recent NZBKing access log, refusals included.
        ///
        /// The refusals matter most: they are the budget declining to spend rather than a
        /// grab failing for some other reason, and nothing else records that distinction.
        /// </summary>
        [HttpGet("nzbking-ledger")]
        [ProducesResponseType(typeof(NzbKingLedgerResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<NzbKingLedgerResponse>> NzbKingLedger(
            [FromQuery] int limit = 25,
            CancellationToken cancellationToken = default)
        {
            var entries = await _browser.GetNzbKingLedgerAsync(
                Math.Clamp(limit, 1, RecentAccessLimit), cancellationToken);

            return Ok(new NzbKingLedgerResponse
            {
                Configured = entries.Count > 0,
                Entries = [.. entries.Select(entry => new NzbKingAccessResponse
                {
                    AttemptedAt = entry.AttemptedAt,
                    Purpose = entry.Purpose.ToString(),
                    Outcome = entry.Outcome.ToString(),
                    Query = entry.Query,
                    HttpStatus = entry.HttpStatus,
                    BalanceAfter = entry.BalanceAfter
                })]
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

        /// <summary>
        /// Short label for a key. The fingerprint is already a hash rather than the key,
        /// but there is no reason to publish all of it to name a row in a list.
        /// </summary>
        private static string Label(string fingerprint) =>
            fingerprint.Length <= 8 ? fingerprint : fingerprint[..8];

        /// <summary>
        /// One sentence an operator can act on, which for a pool means saying how many
        /// keys there are: "about 300 tokens left" reads as alarming or comfortable
        /// depending entirely on whether that is one key or five.
        /// </summary>
        private static string Summarise(
            int keyCount,
            int liveKeys,
            int deletedKeys,
            int balance,
            int spendable,
            DateTime? nextRefillAt)
        {
            if (liveKeys == 0)
            {
                return keyCount == 1
                    ? "The key has been deleted by NZBKing. Request a new one and save it against the source."
                    : $"All {keyCount} keys have been deleted by NZBKing. Request new ones and save them against the source.";
            }

            var ceiling = liveKeys * NzbKingTokenPolicy.MaxTokens;
            var reserve = liveKeys * NzbKingTokenPolicy.ReserveFloor;

            var scope = liveKeys == 1
                ? string.Empty
                : $" across {liveKeys} keys";
            var lost = deletedKeys == 0
                ? string.Empty
                : $" {deletedKeys} further {(deletedKeys == 1 ? "key has" : "keys have")} been deleted.";

            // One token an hour per key, but only while that key is below its cap - a full
            // key earns nothing, so promising liveKeys an hour would overstate it.
            var refill = nextRefillAt is { } due
                ? $" One returns every hour per key below its cap; next at {due:u}."
                : " Every key is at its cap, so there is nothing to wait for.";

            return $"About {balance} of {ceiling} tokens left{scope}, "
                + $"{spendable} of them spendable before the reserve of {reserve}."
                + refill
                + lost;
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
            Chapters = candidate.Post.Chapters,
            Genre = candidate.Post.Genre,
            // Deliberately a flag, not the value: this endpoint never reveals a payload.
            HasPayload = candidate.Post.CanGrab,
            MultiPart = candidate.Post.MultiPart,
            UnrecognisedLabels = candidate.Post.UnrecognisedLabels
        };
    }
}

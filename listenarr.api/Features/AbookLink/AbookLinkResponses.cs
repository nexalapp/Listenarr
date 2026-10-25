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
namespace Listenarr.Api.Features.AbookLink
{
    // What the abook.link endpoints answer with. Split from the controller so neither
    // file has to be read to understand the other, and so the controller stays within the
    // size the architecture tests hold every source file to.

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
        public int? Chapters { get; set; }
        public string? Genre { get; set; }
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

    /// <summary>
    /// One key's standing. NZBKing issues a key per CAPTCHA a person solves, each with its
    /// own allowance, so several keys are several allowances and the client has to count
    /// refills for each of them separately.
    /// </summary>
    public sealed class NzbKingKeyStatusResponse
    {
        /// <summary>
        /// Short label for the key, to tell one row from another. A prefix of the ledger's
        /// fingerprint: enough to distinguish the keys configured here, and not the key.
        /// </summary>
        public string Label { get; set; } = string.Empty;

        public int EstimatedBalance { get; set; }
        public int Spendable { get; set; }
        public DateTime NextRefillAt { get; set; }
        public DateTime? LastSuccessfulUseAt { get; set; }
        public bool KeyDeleted { get; set; }
    }

    public sealed class NzbKingStatusResponse
    {
        public bool Configured { get; set; }

        /// <summary>Pooled estimate: the sum of every key's balance.</summary>
        public int EstimatedBalance { get; set; }

        /// <summary>Per-key ceiling, not the pool's. The pool's is this times KeyCount.</summary>
        public int MaxTokens { get; set; }

        /// <summary>Per-key reserve, held back on every key independently.</summary>
        public int ReserveFloor { get; set; }

        /// <summary>Pooled: what can be spent across every key before any hits its reserve.</summary>
        public int Spendable { get; set; }

        public int KeyCount { get; set; }
        public int DeletedKeyCount { get; set; }

        /// <summary>
        /// Every key, in the order they would be spent. The client counts refills from
        /// these rather than from the pooled figure above: refills land per key, and a
        /// pool of five below their caps earns five an hour, not one.
        /// </summary>
        public List<NzbKingKeyStatusResponse> Keys { get; set; } = new();

        /// <summary>Earliest refill among the keys that can still earn one.</summary>
        public DateTime? NextRefillAt { get; set; }

        public DateTime? LastSuccessfulUseAt { get; set; }

        /// <summary>
        /// True only when every configured key has been deleted, which is when there is no
        /// allowance left at all. One dead key among several is reported by
        /// <see cref="DeletedKeyCount"/>, because the pool can still pay.
        /// </summary>
        public bool KeyDeleted { get; set; }

        public string Summary { get; set; } = string.Empty;

        /// <summary>
        /// Grabs refused in the last 24 hours. A refusal is an event rather than a level:
        /// something was asked for and did not happen, which the balance cannot express.
        /// Spends need no equivalent -- the balance already accounts for them.
        /// </summary>
        public int RefusedRecently { get; set; }
    }

    public sealed class NzbKingAccessResponse
    {
        public DateTime AttemptedAt { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public string? Query { get; set; }
        public int? HttpStatus { get; set; }
        public int BalanceAfter { get; set; }
    }

    public sealed class NzbKingLedgerResponse
    {
        public bool Configured { get; set; }
        public List<NzbKingAccessResponse> Entries { get; set; } = new();
    }

    public sealed class AbookSendResponse
    {
        public int TopicId { get; set; }
        public bool Succeeded { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public bool Thanked { get; set; }
        public bool Sent { get; set; }
        public string? ClientName { get; set; }
        public string? DownloadId { get; set; }
        public bool PasswordSent { get; set; }
        public string? SendDetail { get; set; }
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
}

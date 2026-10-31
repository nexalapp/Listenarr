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

using Listenarr.Application.Audiobooks.Suggestions;
using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Domain.Audiobooks.Health;
using Listenarr.Domain.Audiobooks.Quality;

namespace Listenarr.Application.Audiobooks.Catalog
{
    public class LibraryListService : ILibraryListService
    {
        private static readonly DownloadStatus[] ActiveLibraryDownloadStatuses =
        {
            DownloadStatus.Queued,
            DownloadStatus.Downloading,
            DownloadStatus.Paused,
            DownloadStatus.Processing,
            DownloadStatus.ImportPending
        };

        private readonly IAudiobookRepository _audiobookRepository;
        private readonly IAudiobookFileRepository _audiobookFileRepository;
        private readonly IQualityProfileRepository _qualityProfileRepository;
        private readonly IDownloadRepository _downloadRepository;
        private readonly IConfigurationService? _configurationService;
        private readonly ISuggestionReleaseRepository _releaseChecks;

        public LibraryListService(
            IAudiobookRepository audiobookRepository,
            IAudiobookFileRepository audiobookFileRepository,
            IQualityProfileRepository qualityProfileRepository,
            IDownloadRepository downloadRepository,
            ISuggestionReleaseRepository releaseChecks,
            IConfigurationService? configurationService = null)
        {
            _configurationService = configurationService;
            _audiobookRepository = audiobookRepository;
            _audiobookFileRepository = audiobookFileRepository;
            _qualityProfileRepository = qualityProfileRepository;
            _downloadRepository = downloadRepository;
            _releaseChecks = releaseChecks;
        }

        public async Task<IReadOnlyList<LibraryAudiobookListItem>> GetAllAsync()
        {
            var allAudiobooks = await _audiobookRepository.GetAllAsync();
            var repairOfferThresholdDb = _configurationService == null
                ? AudioQualityThresholds.Default.GenerativeDeficitDb
                : (await _configurationService.GetApplicationSettingsAsync()).GenerativeOfferThresholdDb;
            var audiobooks = allAudiobooks
                .OrderBy(a => a.Title)
                .ToList();

            if (audiobooks.Count == 0)
            {
                return Array.Empty<LibraryAudiobookListItem>();
            }

            // The download repository resolves its own DbContext from IDbContextFactory, so this
            // read can safely run concurrently with the shared-context reads below.
            var activeDownloadTask = _downloadRepository.GetActiveAudiobookIdsAsync(ActiveLibraryDownloadStatuses);

            // File summaries, file counts, and series memberships all execute against the shared
            // scoped ListenArrDbContext, so they must be awaited sequentially: starting a second
            // query on a single DbContext before the first completes throws "a second operation
            // was started on this context instance" under real database latency.
            var fileSummaryRows = await _audiobookFileRepository.GetFormatSummariesAsync();
            var fileCountById = await _audiobookFileRepository.GetCountsByAudiobookIdAsync();
            var chapterHealthById = await _audiobookFileRepository.GetWorstChapterHealthByAudiobookIdAsync();
            var notFoundById = await _audiobookFileRepository.GetNotFoundCountsByAudiobookIdAsync();
            var membershipsByAudiobookId = await _audiobookRepository.GetAllSeriesMembershipsGroupedByAudiobookIdAsync();
            var filesByAudiobookId = fileSummaryRows
                .GroupBy(f => f.AudiobookId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<AudiobookFormatSummary>)g.ToList());

            var qualityProfileIds = audiobooks
                .Where(a => a.QualityProfileId.HasValue)
                .Select(a => a.QualityProfileId!.Value)
                .Distinct()
                .ToArray();

            var allQualityProfiles = await _qualityProfileRepository.GetAllAsync();
            var qualityProfiles = qualityProfileIds.Length == 0
                ? new List<QualityProfile>()
                : allQualityProfiles.Where(q => qualityProfileIds.Contains(q.Id)).ToList();

            var qualityProfilesById = qualityProfiles.ToDictionary(q => q.Id);

            var activeDownloadAudiobookIds = await activeDownloadTask;
            var activeDownloadAudiobookIdSet = activeDownloadAudiobookIds.ToHashSet();

            // One reading of "today" for the whole page, so two books with the same release
            // date cannot land on opposite sides of a midnight boundary within one response.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // What the background check last learned about each ASIN, so a book nothing
            // holds can be told apart from one still worth searching for.
            var availability = await _releaseChecks.GetAllAsync();

            return audiobooks.Select(a =>
            {
                filesByAudiobookId.TryGetValue(a.Id, out var files);
                var hasTrackedFiles = files != null && files.Count > 0;
                var hasLegacyFileSummary = !string.IsNullOrWhiteSpace(a.FilePath);
                var hasAnyFile = hasTrackedFiles || hasLegacyFileSummary;
                var missing = AudiobookWantedEvaluator.Compute(a.Monitored, hasTrackedFiles, a.FilePath);
                SuggestionRelease? availabilityCheck = null;
                if (!string.IsNullOrWhiteSpace(a.Asin))
                {
                    availability.TryGetValue(a.Asin.Trim(), out availabilityCheck);
                }

                // A book nobody holds is still missing, but it is not work waiting to be
                // done, so it leaves the Wanted list rather than sitting in it unanswerable.
                var unobtainable = missing && AudiobookWantedEvaluator.IsUnobtainable(availabilityCheck?.Status);
                var wanted = missing && !unobtainable;
                QualityProfile? qualityProfile = null;
                if (a.QualityProfileId.HasValue)
                {
                    qualityProfilesById.TryGetValue(a.QualityProfileId.Value, out qualityProfile);
                }

                chapterHealthById.TryGetValue(a.Id, out var chapterSummary);
                var health = AudiobookHealth.Judge(new AudiobookHealthFacts(
                    a.AudioAuditVerdict,
                    AudioAuditAcceptance.IsAccepted(a),
                    a.HealthTagsClean,
                    a.HealthTagsReason,
                    a.HealthNamingClean,
                    a.HealthNamingReason,
                    chapterSummary?.Health ?? ChapterHealth.Unknown,
                    a.AudioQuality,
                    a.AudioQualityReason));

                return new LibraryAudiobookListItem
                {
                    Id = a.Id,
                    Title = a.Title,
                    Authors = a.Authors?.ToArray(),
                    Narrators = a.Narrators?.ToArray(),
                    PublishYear = a.PublishYear,
                    PublishedDate = a.PublishedDate,
                    Series = a.Series,
                    SeriesNumber = a.SeriesNumber,
                    SeriesMemberships = membershipsByAudiobookId.TryGetValue(a.Id, out var memberships) && memberships.Count > 0
                        ? memberships.Select(m => new AudiobookSeriesMembershipDto
                        {
                            Id = m.Id,
                            SeriesName = m.SeriesName,
                            SeriesNumber = m.SeriesNumber,
                            SeriesAsin = m.SeriesAsin,
                            IsPrimary = m.IsPrimary,
                            SortOrder = m.SortOrder,
                        }).ToArray()
                        : null,
                    Genres = a.Genres?.ToArray(),
                    Asin = a.Asin,
                    OpenLibraryId = a.OpenLibraryId,
                    Publisher = a.Publisher,
                    Language = a.Language,
                    Runtime = a.Runtime,
                    Edition = a.Edition,
                    ImageUrl = a.ImageUrl,
                    Monitored = a.Monitored,
                    BasePath = a.BasePath,
                    FilePath = a.FilePath,
                    FileSize = a.FileSize,
                    FileCount = fileCountById.TryGetValue(a.Id, out var trueCount) ? trueCount : 0,
                    // Reuses the format summaries already loaded above for status, so
                    // this costs no extra query.
                    Formats = AudiobookFormats.Describe(files),
                    Quality = a.Quality,
                    QualityProfileId = a.QualityProfileId,
                    AuthorAsins = a.AuthorAsins?.ToArray(),
                    Wanted = wanted,
                    ChapterHealth = chapterSummary == null ? null : ChapterHealthNames.Of(chapterSummary.Health),
                    Unobtainable = unobtainable,
                    UnobtainableReason = unobtainable ? availabilityCheck?.Detail : null,
                    UnobtainableCheckedAt = unobtainable ? availabilityCheck?.CheckedAt : null,
                    ChapterRepairable = chapterSummary?.Repairable ?? false,
                    NotFoundFiles = notFoundById.TryGetValue(a.Id, out var notFound) ? notFound : 0,
                    AudioAudit = AudioAuditVerdictNames.Of(a.AudioAuditVerdict),
                    AudioAuditReason = a.AudioAuditReason,
                    AudioAuditHeardNarrator = a.AudioAuditHeardNarrator,
                    AudioAuditAccepted = AudioAuditAcceptance.IsAccepted(a),
                    AudioQuality = a.AudioQuality == Domain.Audiobooks.Quality.AudioQuality.Unknown
                        ? null
                        : AudioQualityNames.Of(a.AudioQuality),
                    AudioQualityReason = a.AudioQualityReason,
                    AudioRepairRecommended = AudioRepairRecommendation.Of(
                        a.AudioQuality, a.AudioQualityMetricsJson, repairOfferThresholdDb),
                    HealthScore = health.Score,
                    HealthTags = AudiobookHealth.NameOf(health.Tags.State),
                    HealthTagsReason = health.Tags.Reason,
                    HealthNaming = AudiobookHealth.NameOf(health.Naming.State),
                    HealthNamingReason = health.Naming.Reason,
                    HealthChapters = AudiobookHealth.NameOf(health.Chapters.State),
                    HealthChaptersReason = health.Chapters.Reason,
                    HealthAudio = AudiobookHealth.NameOf(health.Audio.State),
                    HealthAudioReason = health.Audio.Reason,
                    // Cheap to carry and the cards show it: without these a book's rating
                    // is known only to its own page, and the badge on the cover never appears.
                    AudibleRatingOverall = a.AudibleRatingOverall,
                    AudibleRatingOverallCount = a.AudibleRatingOverallCount,
                    AudnexusRating = a.AudnexusRating,
                    Status = AudiobookStatusEvaluator.ComputeStatus(
                         activeDownloadAudiobookIdSet.Contains(a.Id),
                         hasAnyFile,
                         a.Quality,
                         qualityProfile,
                         files,
                         a.PublishedDate,
                         today)
                };
            }).ToList();
        }

    }
}

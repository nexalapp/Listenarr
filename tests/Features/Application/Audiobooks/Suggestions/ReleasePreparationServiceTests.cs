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
using System.Text.Json;
using Listenarr.Application.Audiobooks.Suggestions;
using Listenarr.Application.Search.AbookLink;
using Listenarr.Application.Search.Nzb;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Suggestions
{
    /// <summary>
    /// Everything a download needs except the sending, done before anyone asks.
    ///
    /// <para>
    /// Two promises matter. Automatic work does not spend a metered allowance unless it
    /// has been allowed to, and a release it could not finish for that reason is marked
    /// as such rather than as "nothing holds this" — the difference decides whether a
    /// grab by hand is worth trying. And what is stored carries a fingerprint of the
    /// payload it came from, because posters replace releases in place.
    /// </para>
    /// </summary>
    [Trait("Name", "ReleasePreparationServiceTests")]
    [Trait("Category", "Application")]
    public sealed class ReleasePreparationServiceTests : BaseTests
    {
        private static readonly DateTime Now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

        private readonly Mock<ISuggestionReleaseRepository> _store = new();
        private readonly Mock<IAbookGrabResolver> _grabs = new();
        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<ISecretProtector> _secrets = new();
        private readonly ApplicationSettings _settings = new();
        private readonly Dictionary<string, SuggestionRelease> _releases = new(StringComparer.OrdinalIgnoreCase);

        private ReleasePreparationService BuildService()
        {
            _configuration.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(_settings);
            _secrets.Setup(s => s.Protect(It.IsAny<string>())).Returns<string>(value => $"enc:{value}");
            _secrets.Setup(s => s.Unprotect(It.IsAny<string>()))
                .Returns<string>(value => value.StartsWith("enc:", StringComparison.Ordinal) ? value[4..] : value);
            _store.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _releases);
            _store.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string asin, CancellationToken _) =>
                    _releases.TryGetValue(asin, out var release) ? release : null);
            _store.Setup(s => s.SaveAsync(It.IsAny<SuggestionRelease>(), It.IsAny<CancellationToken>()))
                .Callback<SuggestionRelease, CancellationToken>((release, _) => _releases[release.Asin] = release)
                .Returns(Task.CompletedTask);

            return new ReleasePreparationService(
                _store.Object,
                _grabs.Object,
                _configuration.Object,
                _secrets.Object,
                new FixedClock(Now),
                NullLogger<ReleasePreparationService>.Instance);
        }

        [Fact]
        public async Task Prepare_OfANewznabHit_IsReadyWithoutOpeningAnything()
        {
            Found("B1", new IndexerSearchResult { Title = "A release", NzbUrl = "https://geek/x.nzb", Source = "NZBgeek" });

            var state = await BuildService().PrepareAsync("B1");

            Assert.Equal(ReleasePreparationState.Ready, state);
            Assert.Equal("enc:https://geek/x.nzb", _releases["B1"].ProtectedNzbUrl);
            // Nothing to thank and nothing to resolve: the indexer handed us the link.
            _grabs.Verify(g => g.ResolveAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Prepare_OfAnAbookPost_ResolvesItAndRemembersThePayloadItCameFrom()
        {
            Found("B1", AbookResult(123463));
            Resolves(123463, "https://nzbking/nzb:abc", "NZBKing", search: "deadbeef");

            var state = await BuildService().PrepareAsync("B1");

            var release = _releases["B1"];
            Assert.Equal(ReleasePreparationState.Ready, state);
            Assert.Equal("enc:https://nzbking/nzb:abc", release.ProtectedNzbUrl);
            Assert.Equal("NZBKing", release.ResolvedVia);
            Assert.Equal(Now, release.PreparedAt);
            // The payload is fingerprinted, never stored: it is the gated value.
            Assert.NotNull(release.PayloadFingerprint);
            Assert.DoesNotContain("deadbeef", release.PayloadFingerprint!);
        }

        [Fact]
        public async Task Prepare_PassesTheMeteredChoiceStraightThrough()
        {
            _settings.PrepareReleasesMayUseMeteredIndexes = true;
            Found("B1", AbookResult(1));
            Resolves(1, "https://nzbking/nzb:abc", "NZBKing");

            await BuildService().PrepareAsync("B1");

            _grabs.Verify(g => g.ResolveAsync(1, true, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Prepare_WithTheMeteredIndexNotAllowed_SaysSoRatherThanCallingItMissing()
        {
            Found("B1", AbookResult(1));
            NotResolved(1, NzbResolutionFailure.NotAsked,
                "Not asked: this index is metered and automatic work is not allowed to spend it.");

            var state = await BuildService().PrepareAsync("B1");

            // The difference decides whether grabbing it by hand is worth a try.
            Assert.Equal(ReleasePreparationState.NeedsMeteredIndex, state);
            _grabs.Verify(g => g.ResolveAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Prepare_WhenNoIndexHoldsIt_IsUnresolvable()
        {
            Found("B1", AbookResult(1));
            NotResolved(1, NzbResolutionFailure.NotIndexed, "No index holds this release.");

            Assert.Equal(ReleasePreparationState.Unresolvable, await BuildService().PrepareAsync("B1"));
        }

        [Fact]
        public async Task Prepare_OfAReleaseWithNeitherLinkNorTopic_IsUnresolvable()
        {
            Found("B1", new IndexerSearchResult { Title = "Nothing to go on" });

            Assert.Equal(ReleasePreparationState.Unresolvable, await BuildService().PrepareAsync("B1"));
        }

        [Fact]
        public async Task PrepareNext_DoesNothingWhileTheFeatureIsOff()
        {
            _settings.PrepareReleasesEnabled = false;
            Found("B1", AbookResult(1));

            Assert.Null(await BuildService().PrepareNextAsync());
            _store.Verify(s => s.SaveAsync(It.IsAny<SuggestionRelease>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PrepareNext_TakesTheMostRecentlyCheckedOneThatIsWaiting()
        {
            Found("OLD", AbookResult(1), checkedAt: Now.AddDays(-2));
            Found("NEW", AbookResult(2), checkedAt: Now);
            Found("DONE", AbookResult(3), checkedAt: Now, prepared: ReleasePreparationState.Ready);
            Resolves(2, "https://nzbking/nzb:abc", "NZBKing");

            await BuildService().PrepareNextAsync();

            _grabs.Verify(g => g.ResolveAsync(2, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void Fingerprint_ChangesWhenThePosterReplacesTheRelease()
        {
            var before = ReleasePreparationService.Fingerprint(new AbookPost { SearchString = "aaaa" });
            var after = ReleasePreparationService.Fingerprint(new AbookPost { SearchString = "bbbb" });
            var password = ReleasePreparationService.Fingerprint(
                new AbookPost { SearchString = "aaaa", Password = "hunter2" });

            Assert.NotNull(before);
            Assert.NotEqual(before, after);
            // A changed password means a changed archive, which is a changed release.
            Assert.NotEqual(before, password);
            Assert.Null(ReleasePreparationService.Fingerprint(new AbookPost()));
        }

        private static IndexerSearchResult AbookResult(int topicId) => new()
        {
            Title = $"Some release {topicId}",
            DownloadReference = topicId.ToString(),
            NzbUrl = string.Empty,
            Source = "Abook.link"
        };

        private void Found(
            string asin,
            IndexerSearchResult result,
            DateTime? checkedAt = null,
            ReleasePreparationState prepared = ReleasePreparationState.NotPrepared) =>
            _releases[asin] = new SuggestionRelease
            {
                Asin = asin,
                Status = SuggestionReleaseStatus.Found,
                ReleaseTitle = result.Title,
                ProtectedResult = $"enc:{JsonSerializer.Serialize(result)}",
                CheckedAt = checkedAt ?? Now,
                Preparation = prepared
            };

        private void Resolves(int topicId, string nzbUrl, string via, string search = "payload") =>
            _grabs
                .Setup(g => g.ResolveAsync(topicId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AbookGrabResult(
                    topicId, true, "Done", $"Resolved via {via}.", true,
                    new AbookPost { SearchString = search },
                    new NzbResolution(true, nzbUrl, via, []),
                    nzbUrl,
                    null));

        private void NotResolved(int topicId, NzbResolutionFailure failure, string detail) =>
            _grabs
                .Setup(g => g.ResolveAsync(topicId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AbookGrabResult(
                    topicId, false, "Resolve NZB", detail, true,
                    new AbookPost { SearchString = "payload" },
                    new NzbResolution(false, null, null,
                        [NzbResolverResult.Failed("NZBKing", failure, detail)]),
                    null,
                    null));

        private sealed class FixedClock(DateTime utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
        }
    }
}

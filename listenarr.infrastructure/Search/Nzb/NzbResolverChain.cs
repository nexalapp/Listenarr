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
using Listenarr.Application.Search.Nzb;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.Nzb
{
    /// <summary>
    /// Asks each resolver in order until one produces an NZB.
    ///
    /// Order matters for cost, not just preference: the free indexes are asked first so a
    /// metered allowance is only spent on releases the free ones genuinely lack. Every
    /// answer is kept, successful or not, because "nothing was found" is not an
    /// actionable message — "NZBIndex had it but incomplete, Binsearch agreed, NZBKing was
    /// out of budget" is.
    /// </summary>
    public class NzbResolverChain : INzbResolverChain
    {
        private readonly IReadOnlyList<INzbResolver> _resolvers;
        private readonly ILogger<NzbResolverChain> _logger;

        public NzbResolverChain(IEnumerable<INzbResolver> resolvers, ILogger<NzbResolverChain> logger)
        {
            ArgumentNullException.ThrowIfNull(resolvers);
            _resolvers = resolvers.OrderBy(r => r.Order).ToList();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<NzbResolution> ResolveAsync(string? searchString, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(searchString))
            {
                return new NzbResolution(false, null, null,
                [
                    NzbResolverResult.Failed("(none)", NzbResolutionFailure.NothingToResolve,
                        "The post did not yield a search string.")
                ]);
            }

            var attempts = new List<NzbResolverResult>();

            foreach (var resolver in _resolvers)
            {
                ct.ThrowIfCancellationRequested();

                var result = await resolver.ResolveAsync(searchString, ct);
                attempts.Add(result);

                if (result.Succeeded && result.NzbUrl is { Length: > 0 })
                {
                    _logger.LogInformation("Resolved {Search} via {Resolver}", searchString, resolver.Name);
                    return new NzbResolution(true, result.NzbUrl, resolver.Name, attempts);
                }

                _logger.LogDebug("{Resolver} did not resolve {Search}: {Detail}",
                    resolver.Name, searchString, result.Detail);
            }

            return new NzbResolution(false, null, null, attempts);
        }

        /// <summary>
        /// Chooses from an index's hits.
        ///
        /// Incomplete collections are rejected: missing parts fail at extraction, after the
        /// bandwidth is spent, and the release may complete later — so it is reported as
        /// "held but incomplete" rather than "not indexed", which are different problems
        /// with different remedies.
        /// </summary>
        internal static NzbResolverResult SelectBest(
            string resolver,
            IReadOnlyList<NzbCandidate> candidates,
            Func<string, string> buildNzbUrl)
        {
            if (candidates.Count == 0)
            {
                return NzbResolverResult.Failed(resolver, NzbResolutionFailure.NotIndexed,
                    "No articles match this search string.");
            }

            var complete = candidates.Where(c => c.Complete != false).ToList();
            if (complete.Count == 0)
            {
                return NzbResolverResult.Failed(resolver, NzbResolutionFailure.OnlyIncomplete,
                    $"Found {candidates.Count} match(es), all missing parts. The release may still be propagating.",
                    candidates);
            }

            // Largest first: a release split into parts lists the collection alongside its
            // fragments, and the collection is the one worth taking.
            var best = complete.OrderByDescending(c => c.SizeBytes ?? 0).First();
            return NzbResolverResult.Found(resolver, buildNzbUrl(best.Id), candidates);
        }
    }
}

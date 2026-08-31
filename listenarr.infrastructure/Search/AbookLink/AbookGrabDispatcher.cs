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
namespace Listenarr.Infrastructure.Search.AbookLink
{
    /// <summary>
    /// Resolves a topic and, if that worked, queues it.
    ///
    /// The two halves stay separable so a failure says which one it was. Resolving is
    /// where a release turns out not to exist yet; queueing is where a client is missing
    /// or refuses. Reporting either as "the grab failed" would leave nothing to act on.
    /// </summary>
    public class AbookGrabDispatcher : IAbookGrabDispatcher
    {
        private readonly IAbookGrabResolver _resolver;
        private readonly AbookDownloadDispatcher _dispatcher;

        public AbookGrabDispatcher(IAbookGrabResolver resolver, AbookDownloadDispatcher dispatcher)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public async Task<AbookSendResult> GrabAsync(int topicId, CancellationToken ct = default)
        {
            var grab = await _resolver.ResolveAsync(topicId, ct);

            if (!grab.Succeeded || grab.NzbUrl is not { Length: > 0 } || grab.Post is null)
            {
                return new AbookSendResult(grab, false, null, null, false, null);
            }

            var sent = await _dispatcher.SendAsync(grab.Post, grab.NzbUrl, ct);

            return new AbookSendResult(
                grab,
                sent.Succeeded,
                sent.ClientName,
                sent.DownloadId,
                sent.PasswordSent,
                sent.Detail);
        }
    }
}

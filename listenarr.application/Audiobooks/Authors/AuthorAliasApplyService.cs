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
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Authors
{
    public sealed record AuthorAliasApplyResult(int BooksChanged, int BooksScanned);

    /// <summary>
    /// Rewrites the library's stored author names through the alias setting once.
    ///
    /// Saves apply aliases on their own from then on; this is for the books already on
    /// the shelf when an alias is added.
    /// </summary>
    public sealed class AuthorAliasApplyService(
        IAudiobookRepository audiobookRepository,
        IConfigurationService configurationService,
        ILogger<AuthorAliasApplyService> logger)
    {
        public async Task<AuthorAliasApplyResult> ApplyAsync(CancellationToken cancellationToken = default)
        {
            var settings = await configurationService.GetApplicationSettingsAsync();
            var aliases = AuthorAliases.Parse(settings.AuthorAliasesJson);
            var audiobooks = await audiobookRepository.GetLibraryAsync();
            var changed = 0;

            foreach (var audiobook in audiobooks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var authors = AuthorAliases.Apply(audiobook.Authors, aliases);
                var narrators = AuthorAliases.Apply(audiobook.Narrators, aliases);
                if (ReferenceEquals(authors, audiobook.Authors) && ReferenceEquals(narrators, audiobook.Narrators))
                {
                    continue;
                }

                audiobook.Authors = authors;
                audiobook.Narrators = narrators;
                await audiobookRepository.UpdateAsync(audiobook);
                changed++;
            }

            logger.LogInformation("Author aliases applied: {Changed} of {Total} books renamed", changed, audiobooks.Count);
            return new AuthorAliasApplyResult(changed, audiobooks.Count);
        }
    }
}

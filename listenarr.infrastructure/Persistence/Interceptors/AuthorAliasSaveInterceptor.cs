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
using Listenarr.Application.Audiobooks.Series;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Listenarr.Infrastructure.Persistence.Interceptors
{
    /// <summary>
    /// Applies the author-alias setting to every audiobook as it is saved.
    ///
    /// The save is the one place every path converges - a provider match, a rescan, an
    /// import, a manual edit - so an alias applied here holds no matter which of them
    /// wrote the name, and no mapper has to remember it. The setting comes from the
    /// snapshot because a save is synchronous with respect to configuration and this
    /// must not open a second context to read it.
    /// </summary>
    public sealed class AuthorAliasSaveInterceptor(
        IApplicationSettingsSnapshot settings,
        ISeriesAuthorSnapshot? seriesAuthors = null) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            Apply(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Apply(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Apply(DbContext? context)
        {
            if (context == null)
            {
                return;
            }

            // Any audiobook write can change who a series files under.
            if (seriesAuthors != null
                && context.ChangeTracker.Entries<Audiobook>().Any(entry =>
                    entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                seriesAuthors.MarkStale();
            }

            var aliases = AuthorAliases.Parse(settings.Current?.AuthorAliasesJson);
            if (aliases.Count == 0)
            {
                return;
            }

            foreach (var entry in context.ChangeTracker.Entries<Audiobook>())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified))
                {
                    continue;
                }

                var authors = AuthorAliases.Apply(entry.Entity.Authors, aliases);
                if (!ReferenceEquals(authors, entry.Entity.Authors))
                {
                    entry.Entity.Authors = authors;
                }

                // The same list covers narrators: a reader credited two ways splits a
                // series across two narrator names just as an author would.
                var narrators = AuthorAliases.Apply(entry.Entity.Narrators, aliases);
                if (!ReferenceEquals(narrators, entry.Entity.Narrators))
                {
                    entry.Entity.Narrators = narrators;
                }
            }
        }
    }
}

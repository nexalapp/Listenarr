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
using Listenarr.Domain.FoundBooks;

namespace Listenarr.Application.FoundBooks.Services
{
    /// <summary>
    /// Which found books sit in a directory that another found book also uses. The
    /// manual import's companion pass takes every non-audio file beside the selected
    /// audio, which is right for a book in its own folder and wrong for a pack's loose
    /// files, where the neighbour's cover would come along. Callers pass companions
    /// only for rows this does not name.
    /// </summary>
    public static class FoundBookFolderSharing
    {
        public static HashSet<int> SharedRows(IReadOnlyList<FoundBook> rows)
        {
            var live = rows
                .Where(r => r.State is not (FoundBookState.Imported or FoundBookState.Discarded))
                .ToList();
            var byDirectory = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in live)
            {
                foreach (var directory in Directories(row))
                {
                    if (!byDirectory.TryGetValue(directory, out var ids))
                    {
                        ids = [];
                        byDirectory[directory] = ids;
                    }

                    ids.Add(row.Id);
                }
            }

            return byDirectory.Values
                .Where(ids => ids.Count > 1)
                .SelectMany(ids => ids)
                .ToHashSet();
        }

        public static IEnumerable<string> Directories(FoundBook row) =>
            FoundBookFilesJson.Deserialize(row.FilesJson)
                .Where(f => f.IsAudio)
                .Select(f => Path.GetDirectoryName(f.Path) ?? row.BookFolder)
                .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

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
namespace Listenarr.Application.Configuration.Contracts
{
    /// <summary>
    /// The application settings as last loaded or saved, for code that renders
    /// synchronously and cannot go to the database for them.
    ///
    /// The naming renderer is the case: it turns metadata into paths and tag values in
    /// plain functions called from a dozen places, and one of its rules - which words to
    /// drop from a series name - is a setting. Threading the setting through every
    /// caller would touch all of them for one string; this holds the last value the
    /// configuration service saw, which it refreshes on every load and save, so the
    /// renderer is never more than one settings read behind.
    /// </summary>
    public interface IApplicationSettingsSnapshot
    {
        ApplicationSettings? Current { get; }

        void Update(ApplicationSettings settings);
    }

    public sealed class ApplicationSettingsSnapshot : IApplicationSettingsSnapshot
    {
        private volatile ApplicationSettings? _current;

        public ApplicationSettings? Current => _current;

        public void Update(ApplicationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            _current = settings;
        }
    }
}

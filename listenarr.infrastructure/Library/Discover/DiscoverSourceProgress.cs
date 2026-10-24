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
using Listenarr.Application.Audiobooks.Discover;

namespace Listenarr.Infrastructure.Library.Discover
{
    /// <summary>
    /// One discovery source's progress through a run.
    ///
    /// <para>
    /// Guarded on every read and write because the page polls it from a request thread
    /// while a detached run writes to it, and a status that tears — a count past its
    /// total, or a run that never stops running — is the kind of thing nobody
    /// reproduces afterwards.
    /// </para>
    /// </summary>
    internal sealed class DiscoverSourceProgress
    {
        private readonly object _gate = new();
        private bool _running;
        private int _completed;
        private int _total;
        private int _failed;
        private string? _current;
        private DateTime? _startedAt;
        private DateTime? _finishedAt;

        public bool Running
        {
            get
            {
                lock (_gate)
                {
                    return _running;
                }
            }
        }

        public string? Current
        {
            get
            {
                lock (_gate)
                {
                    return _current;
                }
            }
            set
            {
                lock (_gate)
                {
                    _current = value;
                }
            }
        }

        public int Completed
        {
            get
            {
                lock (_gate)
                {
                    return _completed;
                }
            }
            set
            {
                lock (_gate)
                {
                    _completed = value;
                }
            }
        }

        public int Failed
        {
            get
            {
                lock (_gate)
                {
                    return _failed;
                }
            }
            set
            {
                lock (_gate)
                {
                    _failed = value;
                }
            }
        }

        public bool TryBegin(int total)
        {
            lock (_gate)
            {
                if (_running)
                {
                    return false;
                }

                _running = true;
                _completed = 0;
                _failed = 0;
                _total = total;
                _current = null;
                _startedAt = DateTime.UtcNow;
                _finishedAt = null;
                return true;
            }
        }

        /// <summary>
        /// Grows the run's total once it knows how much more there is: the posts a page
        /// turned up cannot be counted before the page is read.
        /// </summary>
        public void AddToTotal(int more)
        {
            lock (_gate)
            {
                _total += Math.Max(0, more);
            }
        }

        public void Finish()
        {
            lock (_gate)
            {
                _running = false;
                _current = null;
                _finishedAt = DateTime.UtcNow;
            }
        }

        public DiscoverRefreshStatus Snapshot()
        {
            lock (_gate)
            {
                return new DiscoverRefreshStatus(
                    _running, _completed, _total, _current, _startedAt, _finishedAt, _failed);
            }
        }
    }
}

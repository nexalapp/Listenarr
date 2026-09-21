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
/**
 * The order the Activity page shows work in: what is running, then what runs next,
 * in the order it will run; then what needs a person; then what is done.
 *
 * The API lists jobs newest first, which reads as a stack, while the worker takes
 * them oldest first. Shown as received, the row being worked on sat at the bottom
 * under everything queued behind it. Sorting here puts it at the top, with the
 * queue below it in the order it will be worked through, so the page reads the way
 * the worker behaves. Failures come after the queue because the queue moves and a
 * failure waits; newest first among them, so the latest problem is nearest.
 */

const ACTIVE = new Set([
  'downloading',
  'processing',
  'moving',
  'importpending',
  'importing',
  'paused',
])
const WAITING = new Set(['queued'])
const NEEDS_ATTENTION = new Set(['importblocked', 'failed', 'warning'])

type Orderable = { status?: string | null; addedAt?: string | null }

/** Lower comes first. */
export function activityRank(status: string | null | undefined): number {
  const normalised = (status ?? '').toLowerCase()
  if (ACTIVE.has(normalised)) return 0
  if (WAITING.has(normalised)) return 1
  if (NEEDS_ATTENTION.has(normalised)) return 2
  if (normalised === 'completed' || normalised === 'imported') return 3
  return 1
}

function when(item: Orderable): number {
  const parsed = item.addedAt ? Date.parse(item.addedAt) : Number.NaN
  return Number.isFinite(parsed) ? parsed : Number.NaN
}

/** A stable sort: rank, then time in the direction the rank reads, then arrival. */
export function sortActivity<T extends Orderable>(items: readonly T[]): T[] {
  return items
    .map((item, index) => ({ item, index, rank: activityRank(item.status), at: when(item) }))
    .sort((a, b) => {
      if (a.rank !== b.rank) return a.rank - b.rank
      const aKnown = Number.isFinite(a.at)
      const bKnown = Number.isFinite(b.at)
      if (aKnown && bKnown && a.at !== b.at) {
        // Running and queued: oldest first, the order the worker takes them.
        // Attention and done: newest first, the latest nearest.
        return a.rank <= 1 ? a.at - b.at : b.at - a.at
      }
      if (aKnown !== bKnown) return aKnown ? -1 : 1
      return a.index - b.index
    })
    .map((entry) => entry.item)
}

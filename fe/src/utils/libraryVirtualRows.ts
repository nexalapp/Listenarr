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
import type { Audiobook } from '@/types'
import type { LibraryGroupHeader, LibrarySection } from '@/utils/libraryGrouping'

/**
 * The virtual scroller renders rows, not items, because a grouped library mixes two row
 * shapes of different heights: a heading spanning the full width, and a run of books.
 * Every row carries its own offset so the scroller can place a window of them without
 * assuming a uniform row height.
 */
export type VirtualRow =
  | { type: 'header'; key: string; header: LibraryGroupHeader; top: number; height: number }
  | { type: 'items'; key: string; books: Audiobook[]; top: number; height: number }

export interface VirtualRowHeights {
  itemRow: number
  header: (level: 1 | 2) => number
}

export interface VirtualRowLayout {
  rows: VirtualRow[]
  totalHeight: number
}

export function buildVirtualRows(
  sections: LibrarySection[],
  itemsPerRow: number,
  heights: VirtualRowHeights,
): VirtualRowLayout {
  const perRow = Math.max(1, Math.floor(itemsPerRow) || 1)
  const rows: VirtualRow[] = []
  let top = 0

  for (const section of sections) {
    for (const header of section.headers) {
      const height = heights.header(header.level)
      rows.push({ type: 'header', key: `h:${header.key}`, header, top, height })
      top += height
    }

    for (let index = 0; index < section.books.length; index += perRow) {
      const books = section.books.slice(index, index + perRow)
      rows.push({
        type: 'items',
        key: `r:${section.key}:${books[0]?.id ?? index}`,
        books,
        top,
        height: heights.itemRow,
      })
      top += heights.itemRow
    }
  }

  return { rows, totalHeight: top }
}

/** Index of the first row whose bottom edge is past `offset`. */
function firstRowAt(rows: VirtualRow[], offset: number): number {
  let low = 0
  let high = rows.length - 1
  let found = rows.length
  while (low <= high) {
    const mid = (low + high) >> 1
    const row = rows[mid]!
    if (row.top + row.height > offset) {
      found = mid
      high = mid - 1
    } else {
      low = mid + 1
    }
  }
  return Math.min(found, Math.max(0, rows.length - 1))
}

/**
 * The half-open row range to render for a viewport, padded by `bufferRows` on each side.
 */
export function findVisibleRowRange(
  rows: VirtualRow[],
  scrollTop: number,
  viewportHeight: number,
  bufferRows: number,
): { start: number; end: number } {
  if (rows.length === 0) return { start: 0, end: 0 }

  const first = firstRowAt(rows, Math.max(0, scrollTop))
  let last = first
  const bottom = Math.max(0, scrollTop) + Math.max(0, viewportHeight)
  while (last + 1 < rows.length && rows[last]!.top + rows[last]!.height < bottom) last += 1

  return {
    start: Math.max(0, first - bufferRows),
    end: Math.min(rows.length, last + 1 + bufferRows),
  }
}

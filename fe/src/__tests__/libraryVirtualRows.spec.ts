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
import { describe, expect, it } from 'vitest'
import { buildVirtualRows, findVisibleRowRange } from '@/utils/libraryVirtualRows'
import type { LibrarySection } from '@/utils/libraryGrouping'
import type { Audiobook } from '@/types'

const heights = { itemRow: 100, header: () => 40 }

function books(count: number, offset = 0): Audiobook[] {
  return Array.from(
    { length: count },
    (_, i) => ({ id: offset + i + 1, title: `Book ${offset + i + 1}` }) as Audiobook,
  )
}

function section(key: string, headerCount: number, bookCount: number): LibrarySection {
  return {
    key,
    headers: Array.from({ length: headerCount }, (_, level) => ({
      key: `${key}:${level}`,
      label: key,
      level: (level + 1) as 1 | 2,
      count: bookCount,
    })),
    books: books(bookCount, Number(key)),
  }
}

describe('buildVirtualRows', () => {
  it('stacks heading rows and book rows with running offsets', () => {
    const { rows, totalHeight } = buildVirtualRows([section('0', 1, 5)], 2, heights)

    expect(rows.map((r) => `${r.type}@${r.top}`)).toEqual([
      'header@0',
      'items@40',
      'items@140',
      'items@240',
    ])
    expect(rows[1]!.type === 'items' && rows[1]!.books).toHaveLength(2)
    // The last row holds the odd book out
    expect(rows[3]!.type === 'items' && rows[3]!.books).toHaveLength(1)
    expect(totalHeight).toBe(340)
  })

  it('starts each section on its own row so a heading never shares one', () => {
    const { rows } = buildVirtualRows([section('0', 1, 1), section('10', 2, 1)], 4, heights)

    expect(rows.map((r) => r.type)).toEqual(['header', 'items', 'header', 'header', 'items'])
  })

  it('treats a section with no headings as a plain run of rows', () => {
    const { rows, totalHeight } = buildVirtualRows([section('0', 0, 4)], 4, heights)

    expect(rows).toHaveLength(1)
    expect(totalHeight).toBe(100)
  })
})

describe('findVisibleRowRange', () => {
  const { rows } = buildVirtualRows([section('0', 1, 40)], 2, heights)

  it('covers the viewport plus the buffer on each side', () => {
    // rows: header@0 then 20 item rows of 100 starting at 40
    const range = findVisibleRowRange(rows, 540, 300, 2)

    // 540 lands in row 6 (spanning 540-640); row 8 (740-840) is the last one the
    // viewport touches, so the range runs from 6-2 through 8+1+2.
    expect(rows[range.start]!.top).toBeLessThanOrEqual(540)
    expect(rows[range.end - 1]!.top + rows[range.end - 1]!.height).toBeGreaterThanOrEqual(840)
    expect(range.start).toBe(4)
    expect(range.end).toBe(11)
  })

  it('clamps at both ends of the list', () => {
    expect(findVisibleRowRange(rows, 0, 200, 2).start).toBe(0)
    expect(findVisibleRowRange(rows, 100000, 200, 2).end).toBe(rows.length)
    expect(findVisibleRowRange([], 0, 200, 2)).toEqual({ start: 0, end: 0 })
  })
})

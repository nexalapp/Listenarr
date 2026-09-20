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
import { confidenceLabel, describeRuntimeDelta, matchConfidence } from '@/utils/foundBookMatch'
import type { FoundBook, SearchResult } from '@/types'

function book(overrides: Partial<FoundBook> = {}): FoundBook {
  return {
    id: 1,
    watchFolder: '/downloads',
    bookFolder: '/downloads/Fearless',
    files: [],
    audioFileCount: 2,
    totalBytes: 1,
    totalDurationSeconds: 9 * 3600 + 49 * 60,
    title: 'Fearless',
    author: 'Jack Campbell',
    completeness: 'Complete',
    libraryStatus: 'New',
    state: 'Pending',
    blockedKind: 'None',
    firstSeenAt: '',
    lastSeenAt: '',
    autoAdded: false,
    sharesFolder: false,
    ...overrides,
  }
}

function result(overrides: Partial<SearchResult> = {}): SearchResult {
  return {
    id: 'x',
    title: 'Fearless',
    authors: [{ name: 'Jack Campbell' }],
    runtime: 9 * 3600 + 50 * 60,
    ...overrides,
  } as SearchResult
}

describe('found book match confidence', () => {
  it('is high for the same title, author and running time', () => {
    const c = matchConfidence(result(), book())
    expect(c).toBeGreaterThanOrEqual(0.95)
    expect(confidenceLabel(c)).toBe('matched')
  })

  it('drops to low when the author disagrees and the runtime is off', () => {
    const c = matchConfidence(
      result({ authors: [{ name: 'Someone Else' }], runtime: 11 * 3600 }),
      book(),
    )
    expect(c).toBeLessThan(0.75)
    expect(confidenceLabel(c)).toBe('low')
  })

  it('is certain for a matching ASIN whatever the title says', () => {
    const c = matchConfidence(
      result({ asin: 'B001EPWNEO', title: 'Something Else', authors: [] }),
      book({ asin: 'b001epwneo' }),
    )
    expect(c).toBeGreaterThanOrEqual(0.95)
  })

  it('reads "Heinlein, Robert A" and "Robert A. Heinlein" as one author', () => {
    const c = matchConfidence(
      result({ title: 'Friday', authors: [{ name: 'Robert A. Heinlein' }], runtime: undefined }),
      book({ title: 'Friday', author: 'Heinlein, Robert A' }),
    )
    expect(c).toBeGreaterThanOrEqual(0.85)
  })

  it('describes the runtime difference against the files', () => {
    expect(describeRuntimeDelta(result(), book())).toBe('Runtime within 1m of your files')
    expect(describeRuntimeDelta(result({ runtime: 11 * 3600 + 4 * 60 }), book())).toBe(
      'Runtime differs by 1h 15m',
    )
    expect(describeRuntimeDelta(result({ runtime: undefined }), book())).toBeNull()
  })
})

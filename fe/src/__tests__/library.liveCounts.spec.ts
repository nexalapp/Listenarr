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
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { Audiobook } from '@/types'

vi.mock('@/services/api', () => ({ apiService: {} }))
vi.mock('@/services/signalr', () => ({
  signalRService: {
    onFilesRemoved: vi.fn(() => () => undefined),
    onAudiobookUpdate: vi.fn(() => () => undefined),
    onAudiobookDeleted: vi.fn(() => () => undefined),
  },
}))

import { useLibraryStore } from '@/stores/library'

function book(id: number, fileCount: number): Audiobook {
  return { id, title: `Book ${id}`, fileCount } as Audiobook
}

describe('library store keeps the sidebar count live', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('counts only the books that have a file', () => {
    const store = useLibraryStore()
    store.audiobooks = [book(1, 1), book(2, 0), book(3, 4)]

    expect(store.ownedCount).toBe(2)
  })

  it('counts a wanted book once an import gives it a file', () => {
    const store = useLibraryStore()
    store.audiobooks = [book(1, 1), book(2, 0)]

    // A single book's update carries its files, not a count.
    store.applyAudiobookUpdate({
      id: 2,
      title: 'Book 2',
      files: [{ id: 9 }],
    } as unknown as Audiobook)

    expect(store.ownedCount).toBe(2)
  })

  it('adds a book it has not seen, rather than ignore it', () => {
    const store = useLibraryStore()
    store.audiobooks = [book(1, 1)]

    store.applyAudiobookUpdate(book(7, 1))

    expect(store.audiobooks.map((b) => b.id)).toEqual([1, 7])
    expect(store.ownedCount).toBe(2)
  })

  it('drops a deleted book, and its selection', () => {
    const store = useLibraryStore()
    store.audiobooks = [book(1, 1), book(2, 3)]
    store.toggleSelection(2)

    store.applyAudiobookDeleted(2)

    expect(store.audiobooks.map((b) => b.id)).toEqual([1])
    expect(store.ownedCount).toBe(1)
    expect(store.isSelected(2)).toBe(false)
  })
})

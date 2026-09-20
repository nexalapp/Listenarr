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
import type { FoundBook, SearchResult } from '@/types'

const getFoundBooks = vi.fn()
const getFoundWatchFolders = vi.fn()
const scanFoundBooks = vi.fn()
const foundBookDecision = vi.fn()
const finishFoundBookImport = vi.fn()
const advancedSearch = vi.fn()
const setFoundBookMatch = vi.fn()
const clearFoundBookMatch = vi.fn()
const listenFoundBook = vi.fn()
const addToLibrary = vi.fn()
const updateAudiobook = vi.fn()
const startManualImport = vi.fn()
let changedHandler: ((payload: { pending: number }) => void) | null = null

vi.mock('@/services/api', () => ({
  apiService: {
    getFoundBooks,
    getFoundWatchFolders,
    scanFoundBooks,
    foundBookDecision,
    finishFoundBookImport,
    advancedSearch,
    setFoundBookMatch,
    clearFoundBookMatch,
    listenFoundBook,
    addToLibrary,
    updateAudiobook,
    startManualImport,
    getAudibleMetadata: vi.fn(),
  },
}))

vi.mock('@/services/signalr', () => ({
  signalRService: {
    onFoundBooksChanged: vi.fn((handler) => {
      changedHandler = handler
      return () => {
        changedHandler = null
      }
    }),
  },
}))

vi.mock('@/utils/logger', () => ({
  logger: { debug: vi.fn() },
}))

function book(overrides: Partial<FoundBook> = {}): FoundBook {
  return {
    id: 1,
    watchFolder: '/downloads',
    bookFolder: '/downloads/Hugh Howey - Wool',
    files: [
      { path: '/downloads/Hugh Howey - Wool/01.mp3', length: 10, isAudio: true },
      { path: '/downloads/Hugh Howey - Wool/02.mp3', length: 10, isAudio: true },
      { path: '/downloads/Hugh Howey - Wool/book.nfo', length: 1, isAudio: false },
    ],
    audioFileCount: 2,
    totalBytes: 21,
    totalDurationSeconds: 7200,
    format: 'MP3',
    title: 'Wool',
    author: 'Hugh Howey',
    completeness: 'Complete',
    completenessReason: 'Parts 1–2 of 2.',
    libraryStatus: 'New',
    state: 'Pending',
    blockedKind: 'None',
    firstSeenAt: '2026-09-19T00:00:00Z',
    lastSeenAt: '2026-09-19T00:00:00Z',
    autoAdded: false,
    sharesFolder: false,
    ...overrides,
  }
}

const match: SearchResult = {
  title: 'Wool',
  asin: 'B00ABCDEF1',
  authors: [{ name: 'Hugh Howey' }],
} as SearchResult

async function flush() {
  await new Promise((resolve) => setTimeout(resolve, 0))
  await new Promise((resolve) => setTimeout(resolve, 0))
}

describe('found books store', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    changedHandler = null
    setActivePinia(createPinia())
    getFoundBooks.mockResolvedValue({ items: [book()], pending: 1, blocked: 0, scanning: false })
    advancedSearch.mockResolvedValue([match])
    setFoundBookMatch.mockImplementation(
      async (id: number, m: { asin?: string | null; title?: string | null }) =>
        book({ id, matchAsin: m.asin ?? null, matchTitle: m.title ?? null }),
    )
    clearFoundBookMatch.mockImplementation(async (id: number) => book({ id }))
    addToLibrary.mockResolvedValue({ audiobook: { id: 42 } })
    updateAudiobook.mockResolvedValue({})
    startManualImport.mockResolvedValue({ importedCount: 2, totalCount: 2, results: [] })
    foundBookDecision.mockImplementation(async (id: number, decision: string) => ({
      book: book({
        id,
        state:
          decision === 'begin-import'
            ? 'Importing'
            : decision === 'ignore'
              ? 'Ignored'
              : decision === 'discard'
                ? 'Discarded'
                : 'Pending',
      }),
      skipped: [],
    }))
    finishFoundBookImport.mockResolvedValue({
      book: book({ state: 'Imported', matchedAudiobookId: 42 }),
      skipped: [],
    })
  })

  it('looks up a match for every offered book after loading', async () => {
    const { useFoundBooksStore } = await import('@/stores/foundBooks')
    const store = useFoundBooksStore()

    await store.load()
    await flush()

    expect(advancedSearch).toHaveBeenCalledWith(
      expect.objectContaining({ title: 'Wool', author: 'Hugh Howey' }),
    )
    expect(store.matchState(1).selectedMatch?.asin).toBe('B00ABCDEF1')
    expect(store.addableItems).toHaveLength(1)
  })

  it('remembers the match on the server once a lookup finds one', async () => {
    const { useFoundBooksStore } = await import('@/stores/foundBooks')
    const store = useFoundBooksStore()

    await store.load()
    await flush()

    expect(setFoundBookMatch).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ asin: 'B00ABCDEF1', title: 'Wool', author: 'Hugh Howey' }),
    )
  })

  it('uses a remembered match instead of looking up again', async () => {
    getFoundBooks.mockResolvedValue({
      items: [
        book({
          matchAsin: 'B00REMEMBER',
          matchTitle: 'Wool',
          matchAuthor: 'Hugh Howey',
          matchConfidence: 0.9,
        }),
      ],
      pending: 1,
      blocked: 0,
      scanning: false,
    })
    const { useFoundBooksStore } = await import('@/stores/foundBooks')
    const store = useFoundBooksStore()

    await store.load()
    await flush()

    expect(advancedSearch).not.toHaveBeenCalled()
    expect(store.matchState(1).selectedMatch?.asin).toBe('B00REMEMBER')
    expect(store.matchState(1).confidence).toBe(0.9)
    expect(store.addableItems).toHaveLength(1)
  })

  it('clears the remembered match when the operator clears it', async () => {
    const { useFoundBooksStore } = await import('@/stores/foundBooks')
    const store = useFoundBooksStore()
    await store.load()
    await flush()

    store.selectMatch(1, null)
    await flush()

    expect(clearFoundBookMatch).toHaveBeenCalledWith(1)
    expect(store.matchState(1).selectedMatch).toBeNull()
  })

  it('does not look up books already in the library', async () => {
    getFoundBooks.mockResolvedValue({
      items: [book({ libraryStatus: 'InLibrary', matchedAudiobookId: 9 })],
      pending: 1,
      blocked: 0,
      scanning: false,
    })
    const { useFoundBooksStore } = await import('@/stores/foundBooks')
    const store = useFoundBooksStore()

    await store.load()
    await flush()

    expect(advancedSearch).not.toHaveBeenCalled()
    expect(store.addableItems).toHaveLength(0)
  })

  it('brackets the import with begin and finish, moving only the audio files', async () => {
    const { useFoundBooksStore } = await import('@/stores/foundBooks')
    const store = useFoundBooksStore()
    await store.load()
    await flush()

    const ok = await store.add(1, '/library', true)

    expect(ok).toBe(true)
    expect(foundBookDecision).toHaveBeenCalledWith(1, 'begin-import')
    expect(startManualImport).toHaveBeenCalledWith(
      expect.objectContaining({
        path: '/downloads/Hugh Howey - Wool',
        action: 'move',
        includeCompanionFiles: true,
        cleanupEmptySourceFolders: true,
        items: [
          { fullPath: '/downloads/Hugh Howey - Wool/01.mp3', matchedAudiobookId: 42 },
          { fullPath: '/downloads/Hugh Howey - Wool/02.mp3', matchedAudiobookId: 42 },
        ],
      }),
    )
    expect(finishFoundBookImport).toHaveBeenCalledWith(1, 42)
    expect(store.items[0]?.state).toBe('Imported')
  })

  it('leaves companions behind when the book shares its folder', async () => {
    getFoundBooks.mockResolvedValue({
      items: [book({ sharesFolder: true })],
      pending: 1,
      blocked: 0,
      scanning: false,
    })
    const { useFoundBooksStore } = await import('@/stores/foundBooks')
    const store = useFoundBooksStore()
    await store.load()
    await flush()

    await store.add(1, '/library', true)

    expect(startManualImport).toHaveBeenCalledWith(
      expect.objectContaining({ includeCompanionFiles: false }),
    )
  })

  it('aborts the import and keeps the error when the move fails', async () => {
    startManualImport.mockRejectedValue(new Error('disk full'))
    const { useFoundBooksStore } = await import('@/stores/foundBooks')
    const store = useFoundBooksStore()
    await store.load()
    await flush()

    const ok = await store.add(1, '/library', true)

    expect(ok).toBe(false)
    expect(foundBookDecision).toHaveBeenCalledWith(1, 'abort-import')
    expect(finishFoundBookImport).not.toHaveBeenCalled()
    expect(store.matchState(1).error).toContain('disk full')
  })

  it('refuses to add without a match', async () => {
    advancedSearch.mockResolvedValue([])
    const { useFoundBooksStore } = await import('@/stores/foundBooks')
    const store = useFoundBooksStore()
    await store.load()
    await flush()

    expect(await store.add(1, '/library', true)).toBe(false)
    expect(foundBookDecision).not.toHaveBeenCalled()
  })

  it('reloads when the server says the scan changed something', async () => {
    const { useFoundBooksStore } = await import('@/stores/foundBooks')
    const store = useFoundBooksStore()
    store.subscribe()
    await store.load()
    await flush()
    getFoundBooks.mockResolvedValue({ items: [], pending: 0, blocked: 0, scanning: false })

    changedHandler?.({ pending: 0 })
    await flush()

    expect(store.items).toHaveLength(0)
    expect(store.scanning).toBe(false)
  })

  it('keeps a chosen match across a reload', async () => {
    const { useFoundBooksStore } = await import('@/stores/foundBooks')
    const store = useFoundBooksStore()
    await store.load()
    await flush()
    const chosen = { ...match, asin: 'B00CHOSEN01' } as SearchResult
    store.selectMatch(1, chosen)

    await store.load()
    await flush()

    expect(store.matchState(1).selectedMatch?.asin).toBe('B00CHOSEN01')
    expect(advancedSearch).toHaveBeenCalledTimes(1)
  })
})

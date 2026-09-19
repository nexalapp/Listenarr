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
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { apiService } from '@/services/api'
import { signalRService } from '@/services/signalr'
import { logger } from '@/utils/logger'
import { addAndImportBook } from '@/utils/libraryImportAdd'
import { buildLibraryImportSearchParams } from '@/utils/libraryImportSearch'
import type { FoundBook, FoundBookState, FoundBookWatchFolders, SearchResult } from '@/types'

/** Per-row UI state that the server does not know about: the catalogue match and its lookup. */
export interface FoundBookMatchState {
  selectedMatch: SearchResult | null
  hasSearched: boolean
  searchFailed: boolean
  isSearching: boolean
  separateBook: boolean
  busy: boolean
  error: string | null
}

export type FoundBookFilter = 'pending' | 'blocked' | 'ignored' | 'done'

const LOOKUP_CAP = 5

function emptyMatchState(): FoundBookMatchState {
  return {
    selectedMatch: null,
    hasSearched: false,
    searchFailed: false,
    isSearching: false,
    separateBook: false,
    busy: false,
    error: null,
  }
}

// From a list of search results, prefer the one whose author best matches what the
// files say. Same rule Library Import uses; a wrong author is the commonest bad match.
function pickBestMatch(
  results: SearchResult[],
  detectedAuthor?: string | null,
): SearchResult | null {
  if (!results.length) return null
  if (!detectedAuthor) return results[0] ?? null
  const needle = detectedAuthor.toLowerCase()
  const scored = results.map((r) => {
    const resultAuthor = (r.authors?.[0]?.name ?? '').toLowerCase()
    const match = resultAuthor && (resultAuthor.includes(needle) || needle.includes(resultAuthor))
    return { r, match }
  })
  return scored.find((s) => s.match)?.r ?? results[0] ?? null
}

export function folderName(path: string): string {
  const parts = path.replace(/\\/g, '/').split('/').filter(Boolean)
  return parts[parts.length - 1] ?? path
}

export function stateFilter(state: FoundBookState): FoundBookFilter {
  switch (state) {
    case 'Pending':
    case 'Importing':
      return 'pending'
    case 'Blocked':
      return 'blocked'
    case 'Ignored':
      return 'ignored'
    default:
      return 'done'
  }
}

export const useFoundBooksStore = defineStore('foundBooks', () => {
  const items = ref<FoundBook[]>([])
  const matchStates = ref<Record<number, FoundBookMatchState>>({})
  const pending = ref(0)
  const blocked = ref(0)
  const scanning = ref(false)
  const lastScanCompletedAt = ref<string | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)
  const watchFolders = ref<FoundBookWatchFolders | null>(null)
  const filter = ref<FoundBookFilter>('pending')

  let lookupQueue: number[] = []
  let lookupRunning = false
  let offSignalR: (() => void) | null = null

  const visibleItems = computed(() =>
    items.value.filter((item) => stateFilter(item.state) === filter.value),
  )

  const counts = computed(() => {
    const result: Record<FoundBookFilter, number> = { pending: 0, blocked: 0, ignored: 0, done: 0 }
    for (const item of items.value) result[stateFilter(item.state)]++
    return result
  })

  /** Rows that can be added right now: offered, complete, with a match, and not in the library. */
  const addableItems = computed(() =>
    visibleItems.value.filter(
      (item) =>
        item.state === 'Pending' &&
        item.libraryStatus !== 'InLibrary' &&
        matchStates.value[item.id]?.selectedMatch != null,
    ),
  )

  function matchState(id: number): FoundBookMatchState {
    return matchStates.value[id] ?? emptyMatchState()
  }

  function setMatchState(id: number, patch: Partial<FoundBookMatchState>) {
    matchStates.value = {
      ...matchStates.value,
      [id]: { ...matchState(id), ...patch },
    }
  }

  function replaceItem(book: FoundBook) {
    items.value = items.value.map((item) => (item.id === book.id ? book : item))
  }

  async function load() {
    loading.value = items.value.length === 0
    error.value = null
    try {
      const response = await apiService.getFoundBooks()
      items.value = response.items
      pending.value = response.pending
      blocked.value = response.blocked
      scanning.value = response.scanning
      lastScanCompletedAt.value = response.lastScanCompletedAt ?? null
      // Forget match state for rows that are gone; keep it for the rest so a reload
      // after a scan does not throw away a match the operator just chose.
      const alive = new Set(response.items.map((item) => item.id))
      matchStates.value = Object.fromEntries(
        Object.entries(matchStates.value).filter(([id]) => alive.has(Number(id))),
      )
      queueLookups()
    } catch (e) {
      error.value = (e as Error)?.message ?? 'Failed to load found books'
    } finally {
      loading.value = false
    }
  }

  async function loadWatchFolders() {
    try {
      watchFolders.value = await apiService.getFoundWatchFolders()
    } catch (e) {
      logger.debug('[foundBooks] watch folders failed:', e)
    }
  }

  async function scan() {
    try {
      await apiService.scanFoundBooks()
      scanning.value = true
    } catch (e) {
      const status = (e as { status?: number })?.status
      if (status === 409) {
        scanning.value = true
        return
      }
      error.value = (e as Error)?.message ?? 'Could not start a scan'
    }
  }

  // ─── Catalogue lookups ─────────────────────────────────────────────────────

  function queueLookups() {
    const wanted = items.value
      .filter((item) => item.state === 'Pending' && item.libraryStatus !== 'InLibrary')
      .filter((item) => {
        const state = matchState(item.id)
        return !state.hasSearched && !state.isSearching && !state.searchFailed
      })
      .map((item) => item.id)
    lookupQueue = [...new Set([...lookupQueue, ...wanted])]
    void processLookups()
  }

  async function processLookups() {
    if (lookupRunning) return
    lookupRunning = true
    try {
      while (lookupQueue.length > 0) {
        const id = lookupQueue.shift()!
        const item = items.value.find((candidate) => candidate.id === id)
        if (!item || matchState(id).hasSearched) continue
        await lookup(item)
      }
    } finally {
      lookupRunning = false
    }
  }

  async function lookup(item: FoundBook) {
    setMatchState(item.id, { isSearching: true })
    try {
      const params = buildLibraryImportSearchParams(
        {
          fullPath: item.files.find((f) => f.isAudio)?.path ?? item.bookFolder,
          folderName: folderName(item.bookFolder),
          detectedTitle: item.title ?? undefined,
          detectedAuthor: item.author ?? undefined,
          detectedAsin: item.asin ?? undefined,
        },
        LOOKUP_CAP,
      )
      const results = await apiService.advancedSearch(params)
      // ASIN results are authoritative; otherwise prefer the author the files name.
      const best = params.asin ? (results[0] ?? null) : pickBestMatch(results, item.author)
      setMatchState(item.id, {
        isSearching: false,
        hasSearched: true,
        searchFailed: false,
        selectedMatch: best,
      })
    } catch (e) {
      // A failed lookup is not "not on Audible": keep hasSearched false so a retry asks again.
      logger.debug('[foundBooks] lookup failed:', e)
      setMatchState(item.id, { isSearching: false, searchFailed: true })
    }
  }

  function retryLookups() {
    for (const [id, state] of Object.entries(matchStates.value)) {
      if (state.searchFailed) setMatchState(Number(id), { searchFailed: false })
    }
    queueLookups()
  }

  function selectMatch(id: number, result: SearchResult | null) {
    setMatchState(id, { selectedMatch: result, hasSearched: true, searchFailed: false })
  }

  function setSeparateBook(id: number, value: boolean) {
    setMatchState(id, { separateBook: value })
  }

  // ─── Decisions ─────────────────────────────────────────────────────────────

  async function decide(id: number, decision: 'ignore' | 'restore' | 'discard'): Promise<boolean> {
    setMatchState(id, { busy: true, error: null })
    try {
      const response = await apiService.foundBookDecision(id, decision)
      replaceItem(response.book)
      if (response.skipped.length > 0) {
        setMatchState(id, {
          error: `${response.skipped.length} file(s) were left alone: ${response.skipped[0]}`,
        })
      }
      return true
    } catch (e) {
      setMatchState(id, { error: (e as Error)?.message ?? `Could not ${decision}` })
      return false
    } finally {
      setMatchState(id, { busy: false })
    }
  }

  /**
   * Add one found book to the library: mark it importing, add the matched record,
   * move the files through the manual import, then tell the server to clear what
   * the move left behind.
   */
  async function add(id: number, rootFolderPath: string, monitored: boolean): Promise<boolean> {
    const item = items.value.find((candidate) => candidate.id === id)
    const state = matchState(id)
    if (!item || !state.selectedMatch) return false

    setMatchState(id, { busy: true, error: null })
    let began = false
    try {
      const begun = await apiService.foundBookDecision(id, 'begin-import')
      replaceItem(begun.book)
      began = true

      const result = await addAndImportBook({
        folderPath: item.bookFolder,
        sourceFiles: item.files.filter((f) => f.isAudio).map((f) => f.path),
        match: state.selectedMatch,
        fileMetadata: null,
        rootFolderPath,
        action: 'move',
        monitored,
        separateBook: state.separateBook,
        // A pack's loose files share one directory; the companion pass would sweep the
        // neighbours' covers and notes into this book, so it stays off for those.
        includeCompanionFiles: !item.sharesFolder,
      })

      const finished = await apiService.finishFoundBookImport(id, result.audiobookId)
      replaceItem(finished.book)
      if (finished.skipped.length > 0) {
        setMatchState(id, {
          error: `Imported; ${finished.skipped.length} leftover file(s) could not be removed.`,
        })
      }
      return true
    } catch (e) {
      const message = (e as Error)?.message ?? 'Import failed'
      setMatchState(id, { error: message })
      if (began) {
        // finish-import already put the row back when files remain; abort covers the
        // case where the import never ran. Either way a failure here is not ours to hide.
        try {
          const aborted = await apiService.foundBookDecision(id, 'abort-import')
          replaceItem(aborted.book)
        } catch {
          await load()
        }
      }
      return false
    } finally {
      setMatchState(id, { busy: false })
    }
  }

  async function addAll(
    rootFolderPath: string,
    monitored: boolean,
  ): Promise<{ added: number; failed: number }> {
    let added = 0
    let failed = 0
    for (const item of addableItems.value) {
      if (await add(item.id, rootFolderPath, monitored)) added++
      else failed++
    }
    return { added, failed }
  }

  // ─── Realtime ──────────────────────────────────────────────────────────────

  function subscribe() {
    if (offSignalR) return
    offSignalR = signalRService.onFoundBooksChanged(() => {
      scanning.value = false
      void load()
    })
  }

  function unsubscribe() {
    offSignalR?.()
    offSignalR = null
  }

  return {
    items,
    matchStates,
    pending,
    blocked,
    scanning,
    lastScanCompletedAt,
    loading,
    error,
    watchFolders,
    filter,
    visibleItems,
    counts,
    addableItems,
    matchState,
    load,
    loadWatchFolders,
    scan,
    retryLookups,
    selectMatch,
    setSeparateBook,
    decide,
    add,
    addAll,
    subscribe,
    unsubscribe,
  }
})

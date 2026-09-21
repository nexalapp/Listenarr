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
import { ref } from 'vue'
import { useLibraryStore } from '@/stores/library'
import { useDownloadsStore } from '@/stores/downloads'
import { useToast } from '@/services/toastService'
import { errorTracking } from '@/services/errorTracking'
import type { Audiobook, SearchResult } from '@/types'

export interface ManualSearchTarget {
  audiobook: Audiobook
  /**
   * For a book not yet in the library: adds it on the first grab and returns the
   * id. The search itself needs only a title and authors, so nothing is added -
   * or monitored - unless a release is actually sent to a download client.
   */
  ensureAudiobookId?: () => Promise<number>
}

/**
 * What every list of books lets a person do to one book, in one place, so the
 * Audiobooks page and the author and series pages cannot drift: flip its
 * monitoring from the badge, and search for a release from the cover.
 */
export function useAudiobookActions(component: string) {
  const libraryStore = useLibraryStore()
  const downloadsStore = useDownloadsStore()
  const toast = useToast()

  // Ids in flight keep a double-click from racing two opposite writes; the store
  // shows the flip at once and puts it back on failure.
  const monitorBusy = ref(new Set<number>())

  async function toggleMonitored(audiobook: Audiobook) {
    if (monitorBusy.value.has(audiobook.id)) return
    monitorBusy.value = new Set(monitorBusy.value).add(audiobook.id)
    try {
      await libraryStore.setMonitored(audiobook.id, !audiobook.monitored)
    } catch (e) {
      toast.error(
        'Monitoring not updated',
        `${audiobook.title} was left ${audiobook.monitored ? 'monitored' : 'unmonitored'}.`,
      )
      errorTracking.captureException(e as Error, { component, operation: 'toggleMonitored' })
    } finally {
      const next = new Set(monitorBusy.value)
      next.delete(audiobook.id)
      monitorBusy.value = next
    }
  }

  const manualSearch = ref<ManualSearchTarget | null>(null)

  function openManualSearch(audiobook: Audiobook, ensureAudiobookId?: () => Promise<number>) {
    manualSearch.value = { audiobook, ensureAudiobookId }
  }

  function closeManualSearch() {
    manualSearch.value = null
  }

  function handleManualSearchDownloaded(result: SearchResult) {
    toast.success('Download Added', `${result.title} has been sent to your download client`)
    closeManualSearch()
    void downloadsStore.loadDownloads().catch((e) => {
      errorTracking.captureException(e as Error, {
        component,
        operation: 'handleManualSearchDownloaded',
      })
    })
  }

  return {
    monitorBusy,
    toggleMonitored,
    manualSearch,
    openManualSearch,
    closeManualSearch,
    handleManualSearchDownloaded,
  }
}

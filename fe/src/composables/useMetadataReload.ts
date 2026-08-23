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
import { computed, ref } from 'vue'
import { apiService } from '@/services/api'
import { useToast } from '@/services/toastService'
import { logger } from '@/utils/logger'
import { showConfirm } from '@/composables/useConfirm'

/**
 * Re-reads each book's metadata from its provider and writes it back to the
 * library row.
 *
 * Shared by the audiobooks list and the author and series collections so the
 * action means the same thing everywhere. Note this is not the toolbar's
 * "Refresh", which only re-reads rows Listenarr already holds; this one calls
 * the metadata provider once per book and is therefore slow and rate-limited.
 */

/** Above this many books the operation is slow enough to be worth confirming. */
export const METADATA_RELOAD_CONFIRM_THRESHOLD = 25

/** Providers throttle aggressive callers, so requests are spaced out. */
const REQUEST_SPACING_MS = 250

export interface MetadataReloadTarget {
  id: number
  title?: string
}

export function useMetadataReload() {
  const toast = useToast()

  const isReloading = ref(false)
  const completedCount = ref(0)
  const totalCount = ref(0)

  const progressLabel = computed(() =>
    isReloading.value ? `${completedCount.value}/${totalCount.value}` : '',
  )

  /**
   * Only books Listenarr actually holds can be reloaded. Collection views also
   * render catalogue entries that are not in the library; those carry synthetic
   * negative ids and must be skipped rather than sent to the API.
   */
  function eligibleTargets(candidates: MetadataReloadTarget[]): MetadataReloadTarget[] {
    const seen = new Set<number>()
    return candidates.filter((candidate) => {
      if (!Number.isInteger(candidate.id) || candidate.id <= 0) return false
      if (seen.has(candidate.id)) return false
      seen.add(candidate.id)
      return true
    })
  }

  async function runReload(targets: MetadataReloadTarget[]): Promise<void> {
    isReloading.value = true
    completedCount.value = 0
    totalCount.value = targets.length

    let succeeded = 0
    const failures: string[] = []

    for (const target of targets) {
      try {
        await apiService.rescanAudiobookMetadata(target.id)
        succeeded += 1
      } catch (e) {
        logger.warn('Metadata reload failed', target.id, e)
        failures.push(target.title || `#${target.id}`)
      }
      completedCount.value += 1
      if (completedCount.value < targets.length) {
        await new Promise((resolve) => setTimeout(resolve, REQUEST_SPACING_MS))
      }
    }

    isReloading.value = false

    if (failures.length === 0) {
      toast.success(
        'Metadata reloaded',
        `Updated ${succeeded} audiobook${succeeded === 1 ? '' : 's'}.`,
      )
      return
    }

    // Partial success is the common case when a provider throttles, so report
    // both halves rather than a bare failure.
    toast.warning(
      'Metadata partly reloaded',
      `Updated ${succeeded}, failed ${failures.length}: ${failures.slice(0, 3).join(', ')}${
        failures.length > 3 ? '…' : ''
      }`,
    )
  }

  /**
   * Starts a reload, confirming first when the batch is large enough to be slow.
   */
  async function requestReload(candidates: MetadataReloadTarget[]): Promise<void> {
    if (isReloading.value) return

    const targets = eligibleTargets(candidates)
    if (targets.length === 0) {
      toast.info('Nothing to reload', 'Only audiobooks already in your library can be reloaded.')
      return
    }

    // Small batches run straight away; a large one is slow enough that being
    // asked first is better than a toolbar button silently working for minutes.
    if (targets.length > METADATA_RELOAD_CONFIRM_THRESHOLD) {
      const proceed = await showConfirm(
        `This reloads metadata for ${targets.length} audiobooks, one provider request each. ` +
          'It can take several minutes and may be throttled part way through.',
        'Reload metadata?',
        { confirmText: `Reload ${targets.length}` },
      )
      if (!proceed) return
    }

    await runReload(targets)
  }

  return {
    isReloading,
    completedCount,
    totalCount,
    progressLabel,
    requestReload,
    METADATA_RELOAD_CONFIRM_THRESHOLD,
  }
}

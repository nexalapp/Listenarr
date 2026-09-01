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
import { defineStore } from 'pinia'
import { apiService } from '@/services/api'
import { signalRService } from '@/services/signalr'
import { logger } from '@/utils/logger'
import type { ConversionJobStatus, TagJobUpdate } from '@/types'

export interface TrackedTagJob {
  jobId: string
  audiobookId: number
  status: ConversionJobStatus
  phase: string
  progress: number
  fileCount: number
  tagsWritten: number
  error?: string | null
  failureKind?: string | null
  canRetry: boolean
  /**
   * The job removed a library file and is holding its only replacement. Retrying is
   * what puts it back, so the UI must not present this as an ordinary failure.
   */
  holdingUnpublishedFile: boolean
  trigger: string
}

const terminalStatuses = new Set<ConversionJobStatus>([
  'Completed',
  'Failed',
  'Cancelled',
  'Superseded',
])

function normalizeStatus(status: string | undefined): ConversionJobStatus | null {
  switch ((status || '').trim().toLowerCase()) {
    case 'queued':
      return 'Queued'
    case 'running':
      return 'Running'
    case 'retryscheduled':
      return 'RetryScheduled'
    case 'completed':
      return 'Completed'
    case 'failed':
      return 'Failed'
    case 'cancelled':
      return 'Cancelled'
    case 'superseded':
      return 'Superseded'
    default:
      return null
  }
}

function normalizeJobId(jobId: string): string {
  return jobId.trim().toLowerCase()
}

function toTracked(update: TagJobUpdate, existing?: TrackedTagJob): TrackedTagJob | null {
  const status = normalizeStatus(update.status)
  if (!update.jobId?.trim() || status == null) {
    return null
  }

  const progress = Number.isFinite(update.progress)
    ? Math.min(100, Math.max(0, update.progress as number))
    : (existing?.progress ?? 0)

  return {
    jobId: update.jobId,
    audiobookId: update.audiobookId ?? existing?.audiobookId ?? 0,
    status,
    phase: update.phase ?? existing?.phase ?? 'None',
    // A completed job is complete regardless of the last progress frame it happened
    // to report, which may have been sent before the final flush.
    progress: status === 'Completed' ? 100 : progress,
    fileCount: update.fileCount ?? existing?.fileCount ?? 0,
    tagsWritten: update.tagsWritten ?? existing?.tagsWritten ?? 0,
    error: update.error ?? null,
    failureKind: update.failureKind ?? null,
    canRetry: update.canRetry ?? existing?.canRetry ?? false,
    holdingUnpublishedFile:
      update.holdingUnpublishedFile ?? existing?.holdingUnpublishedFile ?? false,
    trigger: update.trigger ?? existing?.trigger ?? 'Automatic',
  }
}

export const useTagJobsStore = defineStore('tagJobs', () => {
  const trackedById = ref<Record<string, TrackedTagJob>>({})
  let unsubscribe: (() => void) | null = null

  const jobs = computed(() => Object.values(trackedById.value))

  const activeJobs = computed(() => jobs.value.filter((job) => !terminalStatuses.has(job.status)))

  function getJobForAudiobook(audiobookId: number): TrackedTagJob | undefined {
    // Prefer an active job: a book that failed once and is being tagged again has two
    // rows, and the running one is the one a caller cares about.
    return (
      activeJobs.value.find((job) => job.audiobookId === audiobookId) ??
      jobs.value.find((job) => job.audiobookId === audiobookId)
    )
  }

  function apply(update: TagJobUpdate) {
    if (!update?.jobId?.trim()) {
      return
    }

    const key = normalizeJobId(update.jobId)
    const tracked = toTracked(update, trackedById.value[key])
    if (tracked) {
      trackedById.value[key] = tracked
    }
  }

  /**
   * Drop a job the server has removed. The row is gone for good, so waiting for the
   * next refresh would leave the operator looking at something they just dismissed.
   */
  function forget(jobId: string) {
    if (!jobId?.trim()) return
    const key = normalizeJobId(jobId)
    if (key in trackedById.value) {
      const next = { ...trackedById.value }
      delete next[key]
      trackedById.value = next
    }
  }

  async function refresh() {
    try {
      const fetched = await apiService.getTagJobs()
      const next: Record<string, TrackedTagJob> = {}
      for (const update of fetched) {
        const tracked = toTracked(update)
        if (tracked) {
          next[normalizeJobId(tracked.jobId)] = tracked
        }
      }

      trackedById.value = next
    } catch (error) {
      logger.warn('Failed to load tag jobs', error)
    }
  }

  async function write(audiobookId: number, tags?: string[], values?: Record<string, string>) {
    const response = await apiService.writeTags(audiobookId, tags, values)
    if (response.queued) {
      // Refresh rather than synthesising a row: the server owns the job's shape, and a
      // guessed one would show the wrong file count until the first update.
      await refresh()
    }

    return response
  }

  async function retry(jobId: string) {
    const response = await apiService.retryTagWrite(jobId)
    if (response.queued) {
      await refresh()
    }

    return response
  }

  function start() {
    if (unsubscribe) {
      return
    }

    unsubscribe = signalRService.onTagJobUpdate(apply)
    void refresh()
  }

  function stop() {
    unsubscribe?.()
    unsubscribe = null
  }

  return {
    jobs,
    activeJobs,
    getJobForAudiobook,
    apply,
    forget,
    refresh,
    write,
    retry,
    start,
    stop,
  }
})

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
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { failureReasonLabel } from '@/utils/downloadFailures'

/**
 * A failed row says what went wrong. A page of rows all reading "Failed" cannot be
 * triaged without opening each one, which on this library meant 200 of them.
 */
const mountWithFailure = async (failed: Record<string, unknown>[]) => {
  vi.doMock('@/services/signalr', () => ({
    signalRService: { onQueueUpdate: vi.fn(() => () => undefined) },
  }))
  vi.doMock('@/services/api', () => ({
    apiService: {
      getQueue: vi.fn(async () => []),
      removeFromQueue: vi.fn(async () => undefined),
      cancelDownload: vi.fn(async () => undefined),
      retryImport: vi.fn(async () => undefined),
    },
  }))
  vi.doMock('@/stores/configuration', () => ({
    useConfigurationStore: () => ({
      applicationSettings: { showCompletedExternalDownloads: false },
      loadApplicationSettings: vi.fn(async () => undefined),
    }),
  }))
  vi.doMock('@/stores/library', () => ({
    useLibraryStore: () => ({ audiobooks: [], fetchLibrary: vi.fn(async () => undefined) }),
  }))
  const idleJobStore = {
    jobs: [],
    trackedJobs: [],
    activeJobs: [],
    getJobForAudiobook: vi.fn(() => undefined),
    retry: vi.fn(),
    refresh: vi.fn(),
    start: vi.fn(),
  }
  vi.doMock('@/stores/moveJobs', () => ({ useMoveJobsStore: () => idleJobStore }))
  vi.doMock('@/stores/conversionJobs', () => ({ useConversionJobsStore: () => idleJobStore }))
  vi.doMock('@/stores/tagJobs', () => ({ useTagJobsStore: () => idleJobStore }))
  vi.doMock('@/stores/downloads', () => ({
    useDownloadsStore: () => ({
      activeDownloads: [],
      completedDownloads: [],
      failedDownloads: failed,
      loadDownloads: vi.fn(async () => undefined),
    }),
  }))

  const { default: ActivityView } = await import('@/views/activity/ActivityView.vue')
  const wrapper = mount(ActivityView, {
    global: { stubs: { CustomSelect: true, RouterLink: { template: '<a><slot /></a>' } } },
  })
  await flushPromises()
  await new Promise((resolve) => setTimeout(resolve, 0))
  return wrapper
}

const failedDownload = (overrides: Record<string, unknown>) => ({
  id: 'd1',
  title: 'Some.Release.m4b',
  status: 'Failed',
  progress: 0,
  totalSize: 100,
  downloadedSize: 0,
  downloadClientId: 'nzbget-1',
  downloadClientName: 'NZBGet',
  startedAt: new Date().toISOString(),
  metadata: {},
  ...overrides,
})

describe('ActivityView failure reasons', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
    vi.spyOn(globalThis, 'setInterval').mockReturnValue(
      1 as unknown as ReturnType<typeof setInterval>,
    )
    vi.spyOn(globalThis, 'clearInterval').mockImplementation(() => undefined)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('names the reason in place of "Failed"', async () => {
    const wrapper = await mountWithFailure([
      failedDownload({
        failureReason: 'Damaged',
        errorMessage: 'NZBGet failed while verifying or repairing the download.',
      }),
    ])

    expect(wrapper.find('.status-badge').text()).toContain('Damaged')
    expect(wrapper.find('.status-badge').text()).not.toContain('Failed')
  })

  it('still says "Failed" when the client said nothing anyone has classified', async () => {
    const wrapper = await mountWithFailure([
      failedDownload({ failureReason: 'Unknown', errorMessage: 'something went wrong' }),
    ])

    expect(wrapper.find('.status-badge').text()).toContain('Failed')
  })

  it('says "Failed" for a failure recorded before reasons were kept', async () => {
    const wrapper = await mountWithFailure([failedDownload({ errorMessage: 'who knows' })])

    expect(wrapper.find('.status-badge').text()).toContain('Failed')
  })
})

describe('failureReasonLabel', () => {
  it('gives each classified reason a word of its own', () => {
    expect(failureReasonLabel('Health')).toBe('Health')
    expect(failureReasonLabel('Password')).toBe('Password')
    expect(failureReasonLabel('PostProcessing')).toBe('Move failed')
  })

  it('has nothing to add for an unclassified failure', () => {
    expect(failureReasonLabel('Unknown')).toBeNull()
    expect(failureReasonLabel(undefined)).toBeNull()
    expect(failureReasonLabel('SomethingNew')).toBeNull()
  })
})

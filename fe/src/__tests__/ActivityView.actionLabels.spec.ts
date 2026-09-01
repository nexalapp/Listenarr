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

let moveJobsStore: Record<string, unknown>
let conversionJobsStore: Record<string, unknown>
let tagJobsStore: Record<string, unknown>

const mountWith = async (options: {
  audiobooks?: Array<{ id: number; title: string }>
  moveJobs?: unknown[]
  conversionJobs?: unknown[]
  tagJobs?: unknown[]
  activeDownloads?: unknown[]
}) => {
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
    useLibraryStore: () => ({ audiobooks: options.audiobooks ?? [] }),
  }))

  moveJobsStore = { trackedJobs: options.moveJobs ?? [], start: vi.fn() }
  conversionJobsStore = {
    jobs: options.conversionJobs ?? [],
    activeJobs: [],
    getJobForAudiobook: vi.fn(() => undefined),
    retry: vi.fn(),
    refresh: vi.fn(),
    start: vi.fn(),
  }
  tagJobsStore = {
    jobs: options.tagJobs ?? [],
    activeJobs: [],
    getJobForAudiobook: vi.fn(() => undefined),
    retry: vi.fn(),
    refresh: vi.fn(),
    start: vi.fn(),
  }

  vi.doMock('@/stores/moveJobs', () => ({ useMoveJobsStore: () => moveJobsStore }))
  vi.doMock('@/stores/conversionJobs', () => ({
    useConversionJobsStore: () => conversionJobsStore,
  }))
  vi.doMock('@/stores/tagJobs', () => ({ useTagJobsStore: () => tagJobsStore }))
  vi.doMock('@/stores/downloads', () => ({
    useDownloadsStore: () => ({
      activeDownloads: options.activeDownloads ?? [],
      completedDownloads: [],
      failedDownloads: [],
      loadDownloads: vi.fn(async () => undefined),
    }),
  }))

  const { default: ActivityView } = await import('@/views/activity/ActivityView.vue')
  const wrapper = mount(ActivityView, {
    global: {
      stubs: { CustomSelect: true, RouterLink: { template: '<a><slot /></a>' } },
    },
  })
  await flushPromises()
  await new Promise((resolve) => setTimeout(resolve, 0))
  return wrapper
}

describe('ActivityView action labels', () => {
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

  it('says a row is a conversion, and which phase it is in', async () => {
    const wrapper = await mountWith({
      audiobooks: [{ id: 7, title: 'The Gate of the Feral Gods' }],
      conversionJobs: [
        {
          jobId: 'c1',
          audiobookId: 7,
          status: 'Running',
          phase: 'Encoding',
          progress: 42,
          sourceFileCount: 12,
        },
      ],
    })

    const row = wrapper.find('.queue-row')
    // The book is still named — the action is added, not swapped in.
    expect(row.text()).toContain('The Gate of the Feral Gods')
    expect(row.find('.action-label').text()).toBe('Convert to M4B')
    expect(row.find('.action-detail').text()).toContain('Encoding')
    expect(row.find('.action-detail').text()).toContain('12 files')
  })

  it('says a row is a tag write', async () => {
    const wrapper = await mountWith({
      audiobooks: [{ id: 8, title: 'A War of Gifts' }],
      tagJobs: [
        {
          jobId: 't1',
          audiobookId: 8,
          status: 'Running',
          phase: 'Writing',
          progress: 10,
          fileCount: 1,
        },
      ],
    })

    const row = wrapper.find('.queue-row')
    expect(row.text()).toContain('A War of Gifts')
    expect(row.find('.action-label').text()).toBe('Write tags')
    expect(row.find('.action-detail').text()).toContain('Writing tags')
  })

  it('says a row is a library move', async () => {
    const wrapper = await mountWith({
      audiobooks: [{ id: 9, title: 'Drive' }],
      moveJobs: [
        { jobId: 'm1', audiobookId: 9, status: 'Running', phase: 'Verifying', progress: 60 },
      ],
    })

    const row = wrapper.find('.queue-row')
    expect(row.find('.action-label').text()).toBe('Library move')
    expect(row.find('.action-detail').text()).toContain('Verifying')
  })

  it('says a row is a download, and names the client', async () => {
    const wrapper = await mountWith({
      activeDownloads: [
        {
          id: 'd1',
          title: 'Some.Release.m4b',
          status: 'Downloading',
          progress: 30,
          totalSize: 100,
          downloadedSize: 30,
          downloadClientId: 'sab-1',
          downloadClientName: 'SABnzbd',
          startedAt: new Date().toISOString(),
          metadata: {},
        },
      ],
    })

    const row = wrapper.find('.queue-row')
    expect(row.find('.action-label').text()).toBe('Download')
    expect(row.find('.action-detail').text()).toBe('SABnzbd')
  })

  it('distinguishes the three job kinds on screen at once', async () => {
    const wrapper = await mountWith({
      audiobooks: [
        { id: 1, title: 'Book One' },
        { id: 2, title: 'Book Two' },
        { id: 3, title: 'Book Three' },
      ],
      conversionJobs: [
        { jobId: 'c1', audiobookId: 1, status: 'Running', phase: 'Encoding', progress: 1 },
      ],
      tagJobs: [{ jobId: 't1', audiobookId: 2, status: 'Running', phase: 'Writing', progress: 1 }],
      moveJobs: [{ jobId: 'm1', audiobookId: 3, status: 'Running', phase: 'Copying', progress: 1 }],
    })

    const labels = wrapper.findAll('.action-label').map((node) => node.text())
    expect(labels).toContain('Convert to M4B')
    expect(labels).toContain('Write tags')
    expect(labels).toContain('Library move')

    // Each kind is also separable by class, so colour reinforces the word.
    expect(wrapper.find('.action-label.action-conversion').exists()).toBe(true)
    expect(wrapper.find('.action-label.action-tagging').exists()).toBe(true)
    expect(wrapper.find('.action-label.action-move').exists()).toBe(true)
  })

  it('lets the filter find rows by what the action is', async () => {
    const wrapper = await mountWith({
      audiobooks: [
        { id: 1, title: 'Book One' },
        { id: 2, title: 'Book Two' },
      ],
      conversionJobs: [
        { jobId: 'c1', audiobookId: 1, status: 'Running', phase: 'Encoding', progress: 1 },
      ],
      tagJobs: [{ jobId: 't1', audiobookId: 2, status: 'Running', phase: 'Writing', progress: 1 }],
    })

    const vm = wrapper.vm as unknown as { filterText: string; filteredQueue: unknown[] }
    expect(vm.filteredQueue).toHaveLength(2)

    vm.filterText = 'convert'
    await wrapper.vm.$nextTick()
    expect(vm.filteredQueue).toHaveLength(1)
  })
})

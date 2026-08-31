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
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { describe, it, beforeEach, expect, vi } from 'vitest'
import { API_BASE_PATH } from '@/services/apiBase'
import { useLibraryStore } from '@/stores/library'
import { useScanNotificationsStore } from '@/stores/scanNotifications'
import { useFilesystemReadinessStore } from '@/stores/filesystemReadiness'
import { apiService, ensureImageCached } from '@/services/api'
import AudiobookDetailViewCmp from '@/views/library/AudiobookDetailView.vue'
const routerPushMock = vi.fn()
// Mock useRoute to provide params for the detail view
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: '5' } }),
  useRouter: () => ({ push: routerPushMock }),
}))

// Mock api service ensureImageCached and getImageUrl
vi.mock('@/services/api', () => ({
  apiService: {
    getImageUrl: vi.fn((url: string) => url || 'https://via.placeholder.com/300x450?text=No+Image'),
    getQualityProfiles: vi.fn(async () => []),
    getLibrary: vi.fn(async () => []),
    scanAudiobook: vi.fn(),
  },
  ensureImageCached: vi.fn(async () => true),
}))

// Mock signalr service to provide missing hooks (e.g., onScanJobUpdate)
vi.mock('@/services/signalr', () => ({
  signalRService: {
    connect: vi.fn(async () => undefined),
    onQueueUpdate: vi.fn(() => () => undefined),
    onFilesRemoved: vi.fn(() => () => undefined),
    onToast: vi.fn(() => () => undefined),
    onAudiobookUpdate: vi.fn(() => () => undefined),
    onDownloadUpdate: vi.fn(() => () => undefined),
    onDownloadsList: vi.fn(() => () => undefined),
    onScanJobUpdate: vi.fn(() => () => undefined),
    onConversionJobUpdate: vi.fn(() => () => undefined),
    onTagJobUpdate: vi.fn(() => () => undefined),
  },
}))

describe('AudiobookDetailView image recache behavior', () => {
  beforeEach(() => {
    const pinia = createPinia()
    setActivePinia(pinia)
    vi.clearAllMocks()
  })

  it('calls ensureImageCached for the audiobook cover on load', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const imagePath = `${API_BASE_PATH}/images/ASIN000005`
    const store = useLibraryStore()
    store.audiobooks = [
      { id: 5, title: 'Detail Book', imageUrl: imagePath, files: [] },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    store.fetchLibrary = vi.fn(async () => undefined)

    mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    expect(ensureImageCached).toHaveBeenCalled()
    const ensureImageCachedMock = ensureImageCached as unknown as {
      mock: { calls: Array<[string]> }
    }
    expect(ensureImageCachedMock.mock.calls[0]?.[0]).toBe(imagePath)
  })

  it('navigates to the author, narrator, publisher, series, and genre collections when their tags are clicked', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 5,
        title: 'Detail Book',
        authors: ['Brandon Sanderson'],
        narrators: ['Michael Kramer'],
        publisher: 'Tor Audio',
        series: 'Mistborn',
        genres: ['Fantasy'],
        files: [],
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    const authorTag = wrapper
      .findAll('.detail-link-tag')
      .find((tag) => tag.text().includes('Brandon Sanderson'))
    const narratorTag = wrapper
      .findAll('.detail-link-tag')
      .find((tag) => tag.text().includes('Michael Kramer'))
    const publisherTag = wrapper
      .findAll('.detail-link-tag')
      .find((tag) => tag.text().includes('Tor Audio'))
    const seriesTag = wrapper
      .findAll('.detail-link-tag')
      .find((tag) => tag.text().includes('Mistborn'))
    const genreTag = wrapper
      .findAll('.detail-link-tag')
      .find((tag) => tag.text().includes('Fantasy'))

    expect(authorTag).toBeTruthy()
    expect(narratorTag).toBeTruthy()
    expect(publisherTag).toBeTruthy()
    expect(seriesTag).toBeTruthy()
    expect(genreTag).toBeTruthy()

    await authorTag!.trigger('click')

    expect(routerPushMock).toHaveBeenCalledWith('/collection/author/Brandon%20Sanderson')

    await narratorTag!.trigger('click')

    expect(routerPushMock).toHaveBeenCalledWith('/collection/narrator/Michael%20Kramer')

    await publisherTag!.trigger('click')

    expect(routerPushMock).toHaveBeenCalledWith('/collection/publisher/Tor%20Audio')

    await seriesTag!.trigger('click')

    expect(routerPushMock).toHaveBeenCalledWith('/collection/series/Mistborn')

    await genreTag!.trigger('click')

    expect(routerPushMock).toHaveBeenCalledWith('/collection/genre/Fantasy')
  })

  it('names the authors in the hero, ahead of the runtime, each opening its collection', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 5,
        title: 'Detail Book',
        authors: ['Brandon Sanderson', 'Janci Patterson'],
        runtime: 3600,
        files: [],
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    const meta = wrapper.find('.meta-info')
    expect(meta.text().replace(/\s+/g, ' ')).toContain('Brandon Sanderson, Janci Patterson')

    // The authors read before the runtime
    const order = Array.from(meta.element.children).map((c) => c.className)
    expect(order).toEqual(['authors', 'runtime'])

    const links = wrapper.findAll('.meta-author-link')
    expect(links).toHaveLength(2)

    routerPushMock.mockClear()
    await links[1]!.trigger('click')
    expect(routerPushMock).toHaveBeenCalledWith('/collection/author/Janci%20Patterson')
  })

  it('drops a subtitle that only restates the series, leaving the chips', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 5,
        title: 'Howling Dark',
        subtitle: 'Sun Eater, Book 2',
        seriesMemberships: [{ seriesName: 'Sun Eater', seriesNumber: '2', isPrimary: true }],
        files: [],
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    expect(wrapper.find('.subtitle').exists()).toBe(false)
    const chips = wrapper.find('.hero-series')
    expect(chips.exists()).toBe(true)
    expect(chips.text().replace(/\s+/g, ' ')).toContain('Sun Eater #2')
    // "Primary" would be noise on a book with only one series
    expect(chips.text()).not.toContain('Primary')

    routerPushMock.mockClear()
    await chips.find('.detail-link-tag').trigger('click')
    expect(routerPushMock).toHaveBeenCalledWith('/collection/series/Sun%20Eater')
  })

  it('keeps a subtitle that says something the series does not', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 5,
        title: 'A War of Gifts',
        subtitle: 'An Ender Story',
        seriesMemberships: [{ seriesName: 'The Ender Saga', seriesNumber: '10', isPrimary: true }],
        files: [],
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    // A subtitle worth keeping reads above the chips, which always show
    expect(wrapper.find('.subtitle').text()).toBe('An Ender Story')
    expect(wrapper.find('.hero-series').text().replace(/\s+/g, ' ')).toContain('The Ender Saga #10')
  })

  it('names the language once, and only flags a book that is actually abridged', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [
      { id: 5, title: 'Detail Book', language: 'english', abridged: false, files: [] },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    const hero = wrapper.find('.info-section').text()
    expect(hero.match(/English/g)).toHaveLength(1)
    // Every book being "Unabridged" says nothing; only the exception is worth a chip
    expect(hero).not.toContain('Unabridged')
    expect(hero).not.toContain('Abridged')
  })

  it('flags an abridged book, and invents no language for a book that has none', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [
      { id: 5, title: 'Detail Book', abridged: true, files: [] },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    const hero = wrapper.find('.info-section').text()
    expect(hero).toContain('Abridged')
    expect(hero).not.toContain('English')
  })

  it('keeps the monitor toggle on the file details row rather than a row of its own', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [
      { id: 5, title: 'Detail Book', monitored: true, files: [] },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    const rows = Array.from(wrapper.find('.info-section').element.children).map((c) => c.className)
    expect(rows).not.toContain('status-badges')
    expect(wrapper.find('.key-details .hero-monitor-pill').exists()).toBe(true)
  })

  it('shows the series even when the book has no subtitle, and no row when it has no series', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 5,
        title: 'Series Book',
        seriesMemberships: [{ seriesName: 'Sun Eater', seriesNumber: '4', isPrimary: true }],
        files: [],
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']
    store.fetchLibrary = vi.fn(async () => undefined)

    const withSeries = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))
    expect(withSeries.find('.subtitle').exists()).toBe(false)
    expect(withSeries.find('.hero-series').text().replace(/\s+/g, ' ')).toContain('Sun Eater #4')

    store.audiobooks = [
      { id: 5, title: 'Standalone Book', subtitle: 'A True Story', files: [] },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    const standalone = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))
    expect(standalone.find('.subtitle').text()).toBe('A True Story')
    expect(standalone.find('.hero-series').exists()).toBe(false)
  })

  it('names the file formats after the runtime, and nothing when there are no files', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 5,
        title: 'Detail Book',
        runtime: 60,
        files: [{ path: '/audiobooks/a/Part 1.MP3' }, { path: '/audiobooks/a/Part 2.mp3' }],
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']
    store.fetchLibrary = vi.fn(async () => undefined)

    const withFiles = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    const meta = withFiles.find('.meta-info')
    // Distinct containers only, and case does not make two of one format
    expect(meta.find('.formats').text()).toBe('.mp3')
    expect(Array.from(meta.element.children).map((c) => c.className)).toEqual([
      'runtime',
      'formats',
    ])

    // A book whose files the library does not hold yet claims no format
    store.audiobooks = [
      { id: 5, title: 'Missing Book', runtime: 60, files: [] },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    const noFiles = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))
    expect(noFiles.find('.formats').exists()).toBe(false)
  })

  it('falls back to the listing formats when the payload carries no files', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [{ id: 5, title: 'Detail Book', formats: ['M4B'] }] as unknown as ReturnType<
      typeof useLibraryStore
    >['audiobooks']
    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    expect(wrapper.find('.formats').text()).toBe('.m4b')
  })

  it('opens the edit metadata modal from the detail view action', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 5,
        title: 'Detail Book',
        authors: ['Author One'],
        files: [],
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobookDetailViewCmp, {
      global: {
        plugins: [pinia],
        stubs: {
          EditAudiobookModal: {
            name: 'EditAudiobookModal',
            props: ['isOpen'],
            template: '<div class="edit-audiobook-modal-stub" :data-open="String(isOpen)" />',
          },
        },
      },
    })
    await new Promise((r) => setTimeout(r, 10))

    const editButton = wrapper.find('button[aria-label="Edit Metadata"]')
    expect(editButton.exists()).toBe(true)

    await editButton.trigger('click')
    await new Promise((r) => setTimeout(r, 0))

    expect(wrapper.find('.edit-audiobook-modal-stub').attributes('data-open')).toBe('true')
  })

  it('updates the Files tab scan status from the shared scan state', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    const scanNotificationsStore = useScanNotificationsStore()
    store.audiobooks = [{ id: 5, title: 'Detail Book', files: [] }] as unknown as ReturnType<
      typeof useLibraryStore
    >['audiobooks']
    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    const filesTab = wrapper.findAll('.tab').find((tab) => tab.text().includes('Files'))
    expect(filesTab).toBeTruthy()
    await filesTab!.trigger('click')

    scanNotificationsStore.applyUpdate({
      jobId: 'internal-scan-5',
      audiobookId: 5,
      status: 'Processing',
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.scan-job-status').exists()).toBe(false)

    scanNotificationsStore.registerManualScan('scan-job-5', 5)
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.scan-job-status').text()).toContain('scan-job-5')
    expect(wrapper.find('.scan-job-status').text()).toContain('Queued')

    scanNotificationsStore.applyUpdate({
      jobId: 'scan-job-5',
      audiobookId: 5,
      status: 'Completed',
      found: 2,
      created: 1,
    })
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.scan-job-status').text()).toContain('Completed')
    expect(wrapper.find('.scan-job-status').text()).not.toContain('Queued / Processing')

    scanNotificationsStore.clearFinished()
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.scan-job-status').exists()).toBe(false)
  })

  it('shows the newest visible manual scan for the audiobook', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    const scanNotificationsStore = useScanNotificationsStore()
    store.audiobooks = [{ id: 5, title: 'Detail Book', files: [] }] as unknown as ReturnType<
      typeof useLibraryStore
    >['audiobooks']
    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    const filesTab = wrapper.findAll('.tab').find((tab) => tab.text().includes('Files'))
    await filesTab!.trigger('click')

    scanNotificationsStore.registerManualScan('older-scan', 5)
    scanNotificationsStore.applyUpdate({
      jobId: 'older-scan',
      audiobookId: 5,
      status: 'Completed',
    })
    await new Promise((resolve) => setTimeout(resolve, 2))
    scanNotificationsStore.registerManualScan('newer-scan', 5)
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.scan-job-status').text()).toContain('newer-scan')
    expect(wrapper.find('.scan-job-status').text()).toContain('Queued')
    expect(wrapper.find('.scan-job-status').text()).not.toContain('older-scan')
  })

  it('registers an accepted Scan Folder job for global notification progress', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    useFilesystemReadinessStore().readiness = {
      isReady: true,
      status: 'ready',
      databaseConnected: true,
      migrationsCurrent: true,
      errorCode: null,
      filesystemReady: true,
      filesystemStatus: 'Ready',
      filesystemPhase: null,
      filesystemErrorCode: null,
      filesystemErrorMessage: null,
    }
    const store = useLibraryStore()
    const scanNotificationsStore = useScanNotificationsStore()
    store.audiobooks = [{ id: 5, title: 'Detail Book', files: [] }] as unknown as ReturnType<
      typeof useLibraryStore
    >['audiobooks']
    store.fetchLibrary = vi.fn(async () => undefined)
    vi.mocked(apiService.scanAudiobook).mockResolvedValue({
      message: 'Scan enqueued',
      found: 0,
      created: 0,
      jobId: 'scan-job-5',
    })

    const wrapper = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    const scanButton = wrapper.find('button[aria-label="Scan Folder"]')
    expect(scanButton.exists()).toBe(true)
    await scanButton.trigger('click')
    await new Promise((r) => setTimeout(r, 0))

    expect(apiService.scanAudiobook).toHaveBeenCalledWith(5)
    expect(scanNotificationsStore.jobs).toHaveLength(1)
    expect(scanNotificationsStore.jobs[0]).toMatchObject({
      jobId: 'scan-job-5',
      audiobookId: 5,
      status: 'Queued',
      visible: true,
    })
  })

  it('disables Scan Folder while library filesystem initialization is incomplete', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [{ id: 5, title: 'Detail Book', files: [] }] as unknown as ReturnType<
      typeof useLibraryStore
    >['audiobooks']
    store.fetchLibrary = vi.fn(async () => undefined)
    useFilesystemReadinessStore().readiness = {
      isReady: true,
      status: 'ready',
      databaseConnected: true,
      migrationsCurrent: true,
      errorCode: null,
      filesystemReady: false,
      filesystemStatus: 'Running',
      filesystemPhase: 'AudiobookFileIdentities',
      filesystemErrorCode: null,
      filesystemErrorMessage: null,
    }

    const wrapper = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((resolve) => setTimeout(resolve, 10))

    const scanButton = wrapper.get('button[aria-label="Scan Folder"]')
    expect(scanButton.attributes('disabled')).toBeDefined()
    expect(scanButton.attributes('title')).toContain('filesystem initialization')
    await scanButton.trigger('click')
    expect(apiService.scanAudiobook).not.toHaveBeenCalled()
  })
})

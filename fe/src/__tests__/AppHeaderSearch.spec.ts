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
import { mount, type VueWrapper } from '@vue/test-utils'
import { computed } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory, type Router } from 'vue-router'
import { useLibraryStore } from '@/stores/library'
import type { Audiobook } from '@/types'

vi.mock('@/stores/moveJobs', () => ({
  useMoveJobsStore: () => ({
    trackedJobs: [],
    start: vi.fn(),
    stop: vi.fn(),
    loadActiveJobs: vi.fn(async () => undefined),
    activeJobs: [],
  }),
}))

vi.mock('@/stores/libraryDeleteOperations', () => ({
  useLibraryDeleteOperationsStore: () => ({
    operations: [],
    dismiss: vi.fn(),
    clearFinished: vi.fn(),
  }),
}))

vi.mock('@/stores/downloads', () => ({
  useDownloadsStore: () => ({
    activeDownloads: computed(() => []),
    loadDownloads: vi.fn(async () => undefined),
  }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    user: { authenticated: true },
    loadCurrentUser: vi.fn(async () => undefined),
    logout: vi.fn(async () => undefined),
  }),
}))

vi.mock('@/services/signalr', () => ({
  signalRService: {
    connect: vi.fn(async () => undefined),
    onConnected: vi.fn(() => () => undefined),
    onQueueUpdate: vi.fn(() => () => undefined),
    onFilesRemoved: vi.fn(() => () => undefined),
    onScanJobUpdate: vi.fn(() => () => undefined),
    onToast: vi.fn(() => () => undefined),
    onDownloadUpdate: vi.fn(() => () => undefined),
    onDownloadsList: vi.fn(() => () => undefined),
    onNotification: vi.fn(() => () => undefined),
    onConversionJobUpdate: vi.fn(() => () => undefined),
    onTagJobUpdate: vi.fn(() => () => undefined),
  },
}))

vi.mock('@/services/api', () => ({
  apiService: {
    getQueue: vi.fn(async () => []),
    getServiceHealth: vi.fn(async () => ({ version: '0.0.0' })),
    getBootstrapConfig: vi.fn(async () => ({ authenticationRequired: false })),
    getStartupConfig: vi.fn(async () => ({ authenticationRequired: false })),
    getLibrary: vi.fn(async () => []),
    getScanJobStatus: vi.fn(async () => ({ id: 'x', status: 'Queued' })),
    getConversionJobs: vi.fn(async () => []),
    getTagJobs: vi.fn(async () => []),
    getImageUrl: (url: string) => url,
  },
}))

vi.mock('@/router', () => ({
  preloadRoute: vi.fn(),
}))

const book = (seed: Record<string, unknown>): Audiobook => seed as unknown as Audiobook

const LIBRARY = [
  book({
    id: 11,
    title: 'The Atlas Six',
    authors: ['Olivie Blake'],
    authorAsins: ['B08X'],
    imageUrl: '/covers/six.jpg',
    seriesMemberships: [{ seriesName: 'The Atlas', seriesNumber: '1', isPrimary: true }],
  }),
  book({
    id: 12,
    title: 'The Atlas Paradox',
    authors: ['Olivie Blake'],
    authorAsins: ['B08X'],
    imageUrl: '/covers/paradox.jpg',
    seriesMemberships: [{ seriesName: 'The Atlas', seriesNumber: '2', isPrimary: true }],
  }),
]

/**
 * The header search answers for the whole library, so an author and a series are rows of
 * their own next to the books: a name typed there opens the person's page, not the first
 * book that happens to mention them.
 */
describe('App.vue header search', () => {
  let wrapper: VueWrapper | undefined
  let router: Router | undefined

  /** The push a row makes resolves a tick later than the click that caused it. */
  const settled = () => new Promise((resolve) => setTimeout(resolve, 50))

  const typeQuery = async (query: string) => {
    const input = wrapper!.find('.search-input')
    await input.setValue(query)
    await new Promise((resolve) => setTimeout(resolve, 320))
    await wrapper!.vm.$nextTick()
  }

  beforeEach(async () => {
    vi.resetModules()
    const pinia = createPinia()
    setActivePinia(pinia)

    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        { path: '/books/:id', name: 'audiobook-detail', component: { template: '<div />' } },
        { path: '/collection/:type/:name', name: 'collection', component: { template: '<div />' } },
      ],
    })
    await router.push('/')
    await router.isReady().catch(() => {})

    const { default: AppComponent } = await import('@/App.vue')
    wrapper = mount(AppComponent, {
      global: { stubs: ['RouterLink', 'RouterView'], plugins: [pinia, router] },
    })
    await new Promise((resolve) => setTimeout(resolve, 20))

    useLibraryStore().audiobooks = LIBRARY
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    vi.clearAllMocks()
  })

  it('offers the author above their books, with a round portrait', async () => {
    await typeQuery('olivie')

    const rows = wrapper!.findAll('.search-result')
    expect(rows[0]!.text()).toContain('Olivie Blake')
    expect(rows[0]!.text()).toContain('Author · 2 books')
    expect(rows[0]!.find('.result-thumb--author').attributes('src')).toContain('/images/B08X')
    expect(wrapper!.text()).toContain('The Atlas Six')
  })

  it('offers the series with its first two covers stacked, named as a series', async () => {
    await typeQuery('the atlas')

    const series = wrapper!.findAll('.search-result').find((row) => row.text().includes('Series'))
    expect(series).toBeDefined()
    expect(series!.text()).toContain('Series · 2 books')

    const stacked = series!.findAll('.result-stack img')
    expect(stacked).toHaveLength(2)
    // The one in front is the book the series opens with.
    expect(series!.find('.stack-cover--front').attributes('src')).toContain('six.jpg')
    expect(series!.find('.stack-cover--back').attributes('src')).toContain('paradox.jpg')
  })

  it('opens the author page when the author row is clicked', async () => {
    await typeQuery('olivie')
    await wrapper!.findAll('.search-result')[0]!.trigger('click')
    await settled()

    expect(router!.currentRoute.value.fullPath).toBe('/collection/author/Olivie%20Blake')
  })

  it('opens the series page when the series row is clicked', async () => {
    await typeQuery('the atlas')
    const series = wrapper!.findAll('.search-result').find((row) => row.text().includes('Series'))
    await series!.trigger('click')
    await settled()

    expect(router!.currentRoute.value.fullPath).toBe('/collection/series/The%20Atlas')
  })

  it('still opens a book when a book row is clicked', async () => {
    await typeQuery('paradox')
    const bookRow = wrapper!
      .findAll('.search-result')
      .find((row) => row.text().includes('The Atlas Paradox'))
    await bookRow!.trigger('click')
    await settled()

    expect(router!.currentRoute.value.fullPath).toBe('/books/12')
  })
})

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
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import AudiobooksView from '@/views/library/AudiobooksView.vue'
import { useLibraryStore } from '@/stores/library'
// apiService stubbed in vi.mock below if needed

const convertAudiobooksBulkMock = vi.fn()
const saveLibraryCustomFiltersMock = vi.fn(async () => ({ saved: true }))
const getApplicationSettingsMock = vi.fn(async () => ({}) as Record<string, unknown>)
const successToast = vi.fn()
const warningToast = vi.fn()
const errorToast = vi.fn()
const showConfirmMock = vi.fn()

vi.mock('@/services/api', () => ({
  apiService: {
    getQualityProfiles: vi.fn(async () => []),
    getImageUrl: vi.fn((url: string) => url || 'https://via.placeholder.com/300x450?text=No+Image'),
    getBootstrapConfig: vi.fn(async () => ({})),
    getStartupConfig: vi.fn(async () => ({})),
    getApplicationSettings: (...args: unknown[]) => getApplicationSettingsMock(...args),
    saveLibraryCustomFilters: (...args: unknown[]) =>
      saveLibraryCustomFiltersMock(...(args as [unknown[]])),
    convertAudiobooksBulk: (...args: unknown[]) =>
      convertAudiobooksBulkMock(...(args as [number[]])),
    getConversionJobs: vi.fn(async () => []),
  },
}))

vi.mock('@/services/toastService', () => ({
  useToast: () => ({
    success: successToast,
    warning: warningToast,
    error: errorToast,
    info: vi.fn(),
  }),
}))

vi.mock('@/composables/useConfirm', () => ({
  showConfirm: (...args: unknown[]) => showConfirmMock(...args),
}))

type AudiobooksVm = {
  setGroupBy?: (value: string) => Promise<void> | void
  groupByAuthor?: boolean
  groupBySeries?: boolean
  viewMode?: 'grid' | 'list'
  displayedAudiobooks?: Array<{ id: number }>
  librarySections?: Array<{ headers: Array<{ level: number; label: string }> }>
  groupingOptions?: string[]
  groupedCollections?: Array<{
    name: string
    count: number
    coverUrl?: string
    coverUrls?: string[]
    author?: string
  }>
  showItemDetails?: boolean
  groupBy?: string
  visibleRange?: { start: number; end: number }
}

const getVm = (wrapper: ReturnType<typeof mount>) => wrapper.vm as unknown as AudiobooksVm

describe('AudiobooksView', () => {
  beforeEach(() => {
    const pinia = createPinia()
    setActivePinia(pinia)
  })

  it('shows extra details in grid view when showItemDetails is enabled', async () => {
    // ensure ResizeObserver is defined for the mount in vtu
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }
    // Minimal WebSocket stub so SignalRService doesn't throw during tests
    if (typeof (globalThis as unknown as { WebSocket?: unknown }).WebSocket === 'undefined') {
      ;(globalThis as unknown as Record<string, unknown>).WebSocket = function () {
        /* noop */
      }
    }
    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
      ],
    })
    await router.push('/books')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 123,
        title: 'The Test Book',
        authors: ['Test Author'],
        narrators: ['Test Narrator'],
        publisher: 'Test Publisher',
        publishYear: 2020,
        imageUrl: 'https://example.com/cover.jpg',
        files: [],
      },
    ] as unknown as import('@/types').Audiobook[]

    // Persist 'showItemDetails' so component mounts with details on
    localStorage.setItem('listenarr.showItemDetails', 'true')
    // Prevent real fetchLibrary from running during mount (we set audiobooks directly)
    store.fetchLibrary = vi.fn(async () => undefined)
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))

    // Find the rendered extra details block under the poster in the grid
    const bottomDetails = wrapper.find('.grid-bottom-details')
    expect(bottomDetails.exists()).toBe(true)
    expect(wrapper.text()).toContain('The Test Book')
    expect(wrapper.text()).toContain('Test Author')
    expect(wrapper.text()).toContain('Test Narrator')
    expect(wrapper.text()).toContain('Test Publisher')
    expect(wrapper.text()).toContain('2020')
  })
})

describe('AudiobooksView Grouping', () => {
  beforeEach(() => {
    const pinia = createPinia()
    setActivePinia(pinia)
  })

  it('groups audiobooks by author when groupBy is authors', async () => {
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }
    if (typeof (globalThis as unknown as { WebSocket?: unknown }).WebSocket === 'undefined') {
      ;(globalThis as unknown as Record<string, unknown>).WebSocket = function () {
        /* noop */
      }
    }

    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
      ],
    })
    await router.push('/books')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 1,
        title: 'Book 1',
        authors: ['Author A'],
        series: 'Series 1',
        imageUrl: 'cover1.jpg',
        files: [],
      },
      {
        id: 2,
        title: 'Book 2',
        authors: ['Author A'],
        series: 'Series 1',
        imageUrl: 'cover2.jpg',
        files: [],
      },
      {
        id: 3,
        title: 'Book 3',
        authors: ['Author B'],
        series: 'Series 2',
        imageUrl: 'cover3.jpg',
        files: [],
      },
    ] as unknown as import('@/types').Audiobook[]

    store.fetchLibrary = vi.fn(async () => undefined)
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))

    // Set groupBy to authors
    const vm = getVm(wrapper)
    await vm.setGroupBy?.('authors')
    await wrapper.vm.$nextTick()

    const groupedCollections = vm.groupedCollections ?? []
    expect(groupedCollections).toHaveLength(2)
    expect(groupedCollections.find((g) => g.name === 'Author A')).toEqual({
      name: 'Author A',
      count: 2,
      coverUrl: undefined,
    })
    expect(groupedCollections.find((g) => g.name === 'Author B')).toEqual({
      name: 'Author B',
      count: 1,
      coverUrl: undefined,
    })

    // Default sorting when grouped by authors should be author-last ascending
    expect((vm as unknown).sortKey).toBe('author-last')
    expect((vm as unknown).sortOrder).toBe('asc')
  })

  it('groups audiobooks by series when groupBy is series', async () => {
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }
    if (typeof (globalThis as unknown as { WebSocket?: unknown }).WebSocket === 'undefined') {
      ;(globalThis as unknown as Record<string, unknown>).WebSocket = function () {
        /* noop */
      }
    }

    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
      ],
    })
    await router.push('/books')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 1,
        title: 'Book 1',
        authors: ['Author A'],
        series: 'Series 1',
        imageUrl: 'cover1.jpg',
        files: [],
      },
      {
        id: 2,
        title: 'Book 2',
        authors: ['Author A'],
        series: 'Series 1',
        imageUrl: 'cover2.jpg',
        files: [],
      },
      {
        id: 3,
        title: 'Book 3',
        authors: ['Author B'],
        series: 'Series 2',
        imageUrl: 'cover3.jpg',
        files: [],
      },
    ] as unknown as import('@/types').Audiobook[]

    store.fetchLibrary = vi.fn(async () => undefined)
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))

    // Set groupBy to series
    const vm = getVm(wrapper)
    await vm.setGroupBy?.('series')
    await wrapper.vm.$nextTick()

    const groupedCollections = vm.groupedCollections ?? []
    expect(groupedCollections).toHaveLength(2)
    // A series also records the author it is filed under, so the series view can be
    // headed by author.
    expect(groupedCollections.find((g) => g.name === 'Series 1')).toEqual({
      name: 'Series 1',
      count: 2,
      coverUrls: ['cover1.jpg', 'cover2.jpg'],
      author: 'Author A',
    })
    expect(groupedCollections.find((g) => g.name === 'Series 2')).toEqual({
      name: 'Series 2',
      count: 1,
      coverUrls: ['cover3.jpg'],
      author: 'Author B',
    })
  })

  it('updates toolbar sort options and sorts grouped collections by count/name depending on grouping', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
      ],
    })
    await router.push('/books')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    store.audiobooks = [
      { id: 1, title: 'A1', authors: ['Author A'], series: 'Series X', imageUrl: 'c1', files: [] },
      { id: 2, title: 'A2', authors: ['Author A'], series: 'Series X', imageUrl: 'c2', files: [] },
      { id: 3, title: 'B1', authors: ['Author B'], series: 'Series Y', imageUrl: 'c3', files: [] },
    ] as unknown as import('@/types').Audiobook[]

    store.fetchLibrary = vi.fn(async () => undefined)
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))

    const vm = wrapper.vm as unknown as unknown

    // Switch to authors grouping and verify sortOptions exposed for collections
    await vm.setGroupBy('authors')
    await wrapper.vm.$nextTick()

    const optValues = (vm.sortOptions || []).map((o: unknown) => o.value)
    expect(optValues).toContain('author-last')
    expect(optValues).toContain('author-first')
    expect(optValues).toContain('count')

    // Default sorting when grouped by authors should be author-last ascending
    expect((vm as unknown).sortKey).toBe('author-last')
    expect((vm as unknown).sortOrder).toBe('asc')

    // The view options control should not be marked "active" for the default author sort
    const csStub = wrapper.find('view-options-dropdown-stub')
    expect(csStub.exists()).toBe(true)
    expect(csStub.attributes('active')).toBe('false')

    // Sort collections by count descending (non-default) — control should become active
    vm.sortKey = 'count'
    vm.sortOrder = 'desc'
    await wrapper.vm.$nextTick()
    expect(wrapper.find('view-options-dropdown-stub').attributes('active')).toBe('true')
    expect(vm.groupedCollections[0].name).toBe('Author A')

    // Sort collections by author-last ascending (back to default) — control should be inactive
    vm.sortKey = 'author-last'
    vm.sortOrder = 'asc'
    await wrapper.vm.$nextTick()
    expect(wrapper.find('view-options-dropdown-stub').attributes('active')).toBe('false')
    expect(vm.groupedCollections[0].name).toBe('Author A')

    // Switch to series grouping and verify options
    await vm.setGroupBy('series')
    await wrapper.vm.$nextTick()
    const seriesOpt = (vm.sortOptions || []).map((o: unknown) => o.value)
    expect(seriesOpt).toContain('title')
    expect(seriesOpt).toContain('count')
    expect(seriesOpt).not.toContain('author-last')

    // Series default should be `title` ascending and the control should NOT be active
    expect((vm as unknown).sortKey).toBe('title')
    expect((vm as unknown).sortOrder).toBe('asc')
    expect(wrapper.find('view-options-dropdown-stub').attributes('active')).toBe('false')

    // Sort series by count ascending (non-default)
    vm.sortKey = 'count'
    vm.sortOrder = 'asc'
    await wrapper.vm.$nextTick()
    expect(wrapper.find('view-options-dropdown-stub').attributes('active')).toBe('true')
    expect(vm.groupedCollections[0].name).toBe('Series Y')
  })

  it('shows individual books when groupBy is books', async () => {
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }
    if (typeof (globalThis as unknown as { WebSocket?: unknown }).WebSocket === 'undefined') {
      ;(globalThis as unknown as Record<string, unknown>).WebSocket = function () {
        /* noop */
      }
    }

    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
      ],
    })
    await router.push('/books')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 1,
        title: 'Book 1',
        authors: ['Author A'],
        series: 'Series 1',
        imageUrl: 'cover1.jpg',
        files: [],
      },
    ] as unknown as import('@/types').Audiobook[]

    store.fetchLibrary = vi.fn(async () => undefined)
    // Ensure groupBy is 'books'
    localStorage.setItem('listenarr.groupBy', 'books')
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))

    // groupBy defaults to 'books'
    const vm = getVm(wrapper)
    const groupedCollections = vm.groupedCollections ?? []
    expect(groupedCollections).toHaveLength(0)
  })

  it('heads the books list by author and series, in either view mode', async () => {
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }

    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
        {
          path: '/collection/:type/:name',
          name: 'collection',
          component: { template: '<div />' },
        },
      ],
    })
    await router.push('/books')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 1,
        title: 'Zeta',
        authors: ['Ada Lovelace'],
        seriesMemberships: [{ seriesName: 'Engines', seriesNumber: '2', isPrimary: true }],
        files: [],
      },
      {
        id: 2,
        title: 'Alpha',
        authors: ['Ada Lovelace'],
        seriesMemberships: [{ seriesName: 'Engines', seriesNumber: '1', isPrimary: true }],
        files: [],
      },
      { id: 3, title: 'Solo', authors: ['Ada Lovelace'], files: [] },
      { id: 4, title: 'Other', authors: ['Bob Zeta'], files: [] },
    ] as unknown as import('@/types').Audiobook[]

    store.fetchLibrary = vi.fn(async () => undefined)
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))

    const vm = getVm(wrapper)

    // No headings until grouping is switched on
    expect(wrapper.findAll('.group-header')).toHaveLength(0)

    vm.groupByAuthor = true
    vm.groupBySeries = true
    await new Promise((r) => setTimeout(r, 0))
    await wrapper.vm.$nextTick()

    // Only the rows near the (zero-height, under jsdom) viewport are drawn, so the DOM
    // shows the first headings and the row model carries the rest.
    const headings = wrapper.findAll('.group-header').map((h) => h.text())
    expect(headings[0]).toContain('Ada Lovelace')
    expect(headings[1]).toContain('Engines')

    const allHeadings = (vm.librarySections ?? []).flatMap((section) =>
      section.headers.map((h) => `${h.level}:${h.label}`),
    )
    expect(allHeadings).toEqual([
      '1:Ada Lovelace',
      '2:Engines',
      '2:Standalone',
      '1:Bob Zeta',
      '2:Standalone',
    ])

    // Series position wins over the title sort inside a series heading
    expect(vm.displayedAudiobooks?.map((b) => b.id)).toEqual([2, 1, 3, 4])

    // The same headings are drawn in list view
    vm.viewMode = 'list'
    await new Promise((r) => setTimeout(r, 0))
    await wrapper.vm.$nextTick()
    expect(wrapper.findAll('.audiobooks-list .group-header').length).toBeGreaterThan(0)

    // A heading opens the collection page for that author or series
    const link = wrapper.findAll('.group-header-link')[0]!
    expect(link.text()).toBe('Ada Lovelace')
    await link.trigger('click')
    await new Promise((r) => setTimeout(r, 0))
    expect(router.currentRoute.value.fullPath).toBe('/collection/author/Ada%20Lovelace')
  })

  it('searches by series, including a secondary series of a cross-series book', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
      ],
    })
    await router.push('/books')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 1,
        title: 'Drive',
        authors: ['James Corey'],
        imageUrl: 'c1',
        files: [],
        seriesMemberships: [
          { seriesName: 'The Expanse', seriesNumber: '2.7', isPrimary: true, sortOrder: 0 },
          { seriesName: 'Short Fiction', seriesNumber: '3', isPrimary: false, sortOrder: 1 },
        ],
      },
      { id: 2, title: 'Unrelated Book', authors: ['Author B'], imageUrl: 'c2', files: [] },
    ] as unknown as import('@/types').Audiobook[]

    store.fetchLibrary = vi.fn(async () => undefined)
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })

    const vm = wrapper.vm as unknown as unknown

    // The primary series finds the book, and nothing else
    vm.searchQuery = 'expanse'
    await wrapper.vm.$nextTick()
    expect(vm.filteredAndSortedAudiobooks.map((b: { id: number }) => b.id)).toEqual([1])

    // So does its position, with or without the '#'
    vm.searchQuery = 'expanse #2.7'
    await wrapper.vm.$nextTick()
    expect(vm.filteredAndSortedAudiobooks.map((b: { id: number }) => b.id)).toEqual([1])

    // And so does the secondary series, not only the primary
    vm.searchQuery = 'short fiction'
    await wrapper.vm.$nextTick()
    expect(vm.filteredAndSortedAudiobooks.map((b: { id: number }) => b.id)).toEqual([1])
  })

  it("'Clear Filters' button resets search, custom filter and builtin filters", async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
      ],
    })
    await router.push('/books')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    // single audiobook that would be shown when no filters/search applied
    store.audiobooks = [
      { id: 1, title: 'Visible Book', authors: ['Author A'], imageUrl: 'c1', files: [] },
    ] as unknown as import('@/types').Audiobook[]

    store.fetchLibrary = vi.fn(async () => undefined)
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })

    const vm = wrapper.vm as unknown as unknown

    // Apply a search that yields no results and a custom filter selection
    vm.searchQuery = 'no-match-query'
    vm.selectedFilterId = 'custom-1'
    vm.filterMonitored = 'monitored'
    await wrapper.vm.$nextTick()

    // Should show the 'No audiobooks match your filters' empty state
    expect(wrapper.text()).toContain('No audiobooks match your filters')

    // Click the Clear Filters button and verify everything resets
    const clearBtn = wrapper.find('button.btn.btn-primary')
    expect(clearBtn.exists()).toBe(true)
    expect(clearBtn.text()).toContain('Clear Filters')

    await clearBtn.trigger('click')
    await wrapper.vm.$nextTick()

    expect(vm.searchQuery).toBe('')
    expect(vm.selectedFilterId).toBeNull()
    expect(vm.filterMonitored).toBe('all')

    // After clearing, the audiobook should be visible again
    expect(wrapper.text()).toContain('Visible Book')
  })

  it('takes its grouping from the route and re-groups when the route changes', async () => {
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }
    if (typeof (globalThis as unknown as { WebSocket?: unknown }).WebSocket === 'undefined') {
      ;(globalThis as unknown as Record<string, unknown>).WebSocket = function () {
        /* noop */
      }
    }

    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', redirect: { name: 'books' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
        {
          path: '/authors',
          name: 'authors',
          component: AudiobooksView,
          meta: { libraryGroup: 'authors' },
        },
        {
          path: '/series',
          name: 'series',
          component: AudiobooksView,
          meta: { libraryGroup: 'series' },
        },
      ],
    })
    // Start on a non-default grouping so the assertion cannot pass by accident.
    await router.push('/series')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 1,
        title: 'Book 1',
        authors: ['Author A'],
        series: 'Series 1',
        imageUrl: 'cover1.jpg',
        files: [],
      },
    ] as unknown as import('@/types').Audiobook[]

    store.fetchLibrary = vi.fn(async () => undefined)
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))

    expect(getVm(wrapper).groupBy).toBe('series')

    // Navigating is what changes the grouping; there is no other source of truth.
    await router.push('/authors')
    await new Promise((r) => setTimeout(r, 0))
    await wrapper.vm.$nextTick()

    expect(getVm(wrapper).groupBy).toBe('authors')
  })

  it('resets the virtual range when returning to books grouping', async () => {
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }
    if (typeof (globalThis as unknown as { WebSocket?: unknown }).WebSocket === 'undefined') {
      ;(globalThis as unknown as Record<string, unknown>).WebSocket = function () {
        /* noop */
      }
    }

    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
        {
          path: '/authors',
          name: 'authors',
          component: AudiobooksView,
          meta: { libraryGroup: 'authors' },
        },
      ],
    })
    // Start grouped by authors so switching back to books is a real change.
    await router.push('/authors')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    store.audiobooks = Array.from({ length: 50 }, (_, index) => ({
      id: index + 1,
      title: `Book ${index + 1}`,
      authors: [`Author ${index % 5}`],
      imageUrl: `cover${index + 1}.jpg`,
      files: [],
    })) as unknown as import('@/types').Audiobook[]

    store.fetchLibrary = vi.fn(async () => undefined)
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))

    const vm = getVm(wrapper)
    vm.visibleRange = { start: 40, end: 50 }
    await vm.setGroupBy?.('books')
    await wrapper.vm.$nextTick()

    expect(vm.groupBy).toBe('books')
    expect(vm.visibleRange?.start).toBe(0)
  })

  it('clears selection when changing grouping mode', async () => {
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }
    if (typeof (globalThis as unknown as { WebSocket?: unknown }).WebSocket === 'undefined') {
      ;(globalThis as unknown as Record<string, unknown>).WebSocket = function () {
        /* noop */
      }
    }

    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
      ],
    })
    await router.push('/books')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 1,
        title: 'Book 1',
        authors: ['Author A'],
        series: 'Series 1',
        imageUrl: 'cover1.jpg',
        files: [],
      },
      {
        id: 2,
        title: 'Book 2',
        authors: ['Author B'],
        series: 'Series 2',
        imageUrl: 'cover2.jpg',
        files: [],
      },
    ] as unknown as import('@/types').Audiobook[]

    store.fetchLibrary = vi.fn(async () => undefined)
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))

    // Select one item
    store.toggleSelection(1)
    expect(store.selectedIds.size).toBeGreaterThan(0)

    // Switch group and expect selection cleared
    const vm = getVm(wrapper)
    await vm.setGroupBy?.('authors')
    await wrapper.vm.$nextTick()
    expect(store.selectedIds.size).toBe(0)
  })

  it('lays collections out as cards or rows, following the view toggle', async () => {
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }

    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/authors',
          name: 'authors',
          component: AudiobooksView,
          meta: { requiresAuth: true, libraryGroup: 'authors' },
        },
        {
          path: '/collection/:type/:name',
          name: 'collection',
          component: { template: '<div />' },
        },
      ],
    })
    await router.push('/authors')
    await router.isReady().catch(() => {})

    localStorage.setItem('listenarr.viewMode', 'grid')

    const store = useLibraryStore()
    store.audiobooks = [
      { id: 1, title: 'Book 1', authors: ['Author A'], imageUrl: 'c1', files: [] },
      { id: 2, title: 'Book 2', authors: ['Author A'], imageUrl: 'c2', files: [] },
      { id: 3, title: 'Book 3', authors: ['Author B'], imageUrl: 'c3', files: [] },
    ] as unknown as import('@/types').Audiobook[]

    store.fetchLibrary = vi.fn(async () => undefined)
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))

    expect(wrapper.findAll('.grouped-grid .collection-card')).toHaveLength(2)
    expect(wrapper.find('.grouped-list').exists()).toBe(false)

    const vm = getVm(wrapper)
    vm.viewMode = 'list'
    await wrapper.vm.$nextTick()

    const rows = wrapper.findAll('.collection-list-item')
    expect(rows).toHaveLength(2)
    expect(wrapper.find('.grouped-grid').exists()).toBe(false)
    expect(rows[0]!.text()).toContain('Author A')
    expect(rows[0]!.text()).toContain('2 books')

    // A row opens the same collection page the card does
    await rows[0]!.trigger('click')
    await new Promise((r) => setTimeout(r, 0))
    expect(router.currentRoute.value.fullPath).toBe('/collection/author/Author%20A')
  })

  it('heads the series view by author, and offers that grouping only where it applies', async () => {
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }

    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
        {
          path: '/authors',
          name: 'authors',
          component: AudiobooksView,
          meta: { libraryGroup: 'authors' },
        },
        {
          path: '/series',
          name: 'series',
          component: AudiobooksView,
          meta: { libraryGroup: 'series' },
        },
      ],
    })
    await router.push('/series')
    await router.isReady().catch(() => {})

    localStorage.setItem('listenarr.viewMode', 'list')

    const store = useLibraryStore()
    store.audiobooks = [
      { id: 1, title: 'A1', authors: ['Ada Lovelace'], series: 'Engines', files: [] },
      { id: 2, title: 'A2', authors: ['Ada Lovelace'], series: 'Looms', files: [] },
      { id: 3, title: 'B1', authors: ['Bob Zeta'], series: 'Widgets', files: [] },
    ] as unknown as import('@/types').Audiobook[]

    store.fetchLibrary = vi.fn(async () => undefined)
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))

    const vm = getVm(wrapper)

    // The series view offers only the author grouping; the authors view offers none
    expect(vm.groupingOptions).toEqual(['author'])

    vm.groupByAuthor = true
    await wrapper.vm.$nextTick()

    const headings = wrapper.findAll('.collection-group-header').map((h) => h.text())
    expect(headings[0]).toContain('Ada Lovelace')
    expect(headings[0]).toContain('2 series')
    expect(headings[1]).toContain('Bob Zeta')

    // Turning it off puts the collections back in one flat run
    vm.groupByAuthor = false
    await wrapper.vm.$nextTick()
    expect(wrapper.findAll('.collection-group-header')).toHaveLength(0)
    expect(wrapper.findAll('.collection-list-item')).toHaveLength(3)

    await vm.setGroupBy?.('authors')
    await wrapper.vm.$nextTick()
    expect(vm.groupingOptions).toEqual([])
  })

  it('series bottom placard is only visible when showItemDetails is enabled', async () => {
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }
    if (typeof (globalThis as unknown as { WebSocket?: unknown }).WebSocket === 'undefined') {
      ;(globalThis as unknown as Record<string, unknown>).WebSocket = function () {
        /* noop */
      }
    }

    // Ensure persisted item details are cleared for this test (deterministic)
    localStorage.setItem('listenarr.showItemDetails', 'false')

    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
      ],
    })
    await router.push('/books')
    await router.isReady().catch(() => {})

    // The placard belongs to the card layout, so pin it rather than inheriting the
    // view mode another test persisted.
    localStorage.setItem('listenarr.viewMode', 'grid')

    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 1,
        title: 'Book 1',
        authors: ['Author A'],
        series: 'Series 1',
        imageUrl: 'cover1.jpg',
        files: [],
      },
      {
        id: 2,
        title: 'Book 2',
        authors: ['Author B'],
        series: 'Series 2',
        imageUrl: 'cover2.jpg',
        files: [],
      },
    ] as unknown as import('@/types').Audiobook[]

    store.fetchLibrary = vi.fn(async () => undefined)
    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))

    // Set groupBy to series
    const vm = getVm(wrapper)
    await vm.setGroupBy?.('series')
    await wrapper.vm.$nextTick()

    // By default, details should be hidden and placard not present
    expect(vm.showItemDetails).toBe(false)
    expect(wrapper.find('.series-bottom-placard').exists()).toBe(false)

    // Enable details and confirm placard is shown
    if (vm) {
      vm.showItemDetails = true
    }
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.series-bottom-placard').exists()).toBe(true)
  })
})

describe('AudiobooksView Filter Persistence', () => {
  const SELECTED_FILTER_KEY = 'listenarr.selectedFilter'
  const CUSTOM_FILTERS_KEY = 'listenarr.customFilters'

  const mountView = async () => {
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }
    if (typeof (globalThis as unknown as { WebSocket?: unknown }).WebSocket === 'undefined') {
      ;(globalThis as unknown as Record<string, unknown>).WebSocket = function () {
        /* noop */
      }
    }

    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
      ],
    })
    await router.push('/books')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    store.audiobooks = [
      { id: 1, title: 'Watched Book', authors: ['Author A'], monitored: true, files: [] },
      { id: 2, title: 'Ignored Book', authors: ['Author B'], monitored: false, files: [] },
    ] as unknown as import('@/types').Audiobook[]
    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))
    return wrapper
  }

  beforeEach(() => {
    localStorage.removeItem(SELECTED_FILTER_KEY)
    localStorage.removeItem(CUSTOM_FILTERS_KEY)
    localStorage.removeItem('listenarr.searchQuery')
    const pinia = createPinia()
    setActivePinia(pinia)
  })

  it('restores a built-in filter selected in an earlier session', async () => {
    localStorage.setItem(SELECTED_FILTER_KEY, 'unmonitored')

    const wrapper = await mountView()

    expect(
      (getVm(wrapper) as unknown as { selectedFilterId: string | null }).selectedFilterId,
    ).toBe('unmonitored')
    expect(wrapper.text()).toContain('Ignored Book')
    expect(wrapper.text()).not.toContain('Watched Book')
  })

  it('restores a custom filter that still exists', async () => {
    localStorage.setItem(
      CUSTOM_FILTERS_KEY,
      JSON.stringify([{ id: 'cf-1', label: 'Mine', rules: [] }]),
    )
    localStorage.setItem(SELECTED_FILTER_KEY, 'cf-1')

    const wrapper = await mountView()

    expect(
      (getVm(wrapper) as unknown as { selectedFilterId: string | null }).selectedFilterId,
    ).toBe('cf-1')
  })

  it('discards a stored filter whose custom filter has been deleted', async () => {
    localStorage.setItem(SELECTED_FILTER_KEY, 'cf-gone')

    const wrapper = await mountView()

    expect(
      (getVm(wrapper) as unknown as { selectedFilterId: string | null }).selectedFilterId,
    ).toBeNull()
    expect(localStorage.getItem(SELECTED_FILTER_KEY)).toBeNull()
    expect(wrapper.text()).toContain('Watched Book')
    expect(wrapper.text()).toContain('Ignored Book')
  })

  it('persists a newly selected filter and clears it when reset', async () => {
    const wrapper = await mountView()
    const vm = getVm(wrapper) as unknown as { selectedFilterId: string | null }

    vm.selectedFilterId = 'missing'
    await wrapper.vm.$nextTick()
    expect(localStorage.getItem(SELECTED_FILTER_KEY)).toBe('missing')

    vm.selectedFilterId = null
    await wrapper.vm.$nextTick()
    expect(localStorage.getItem(SELECTED_FILTER_KEY)).toBeNull()
  })
})

describe('AudiobooksView Bulk Conversion', () => {
  const mountWithSelection = async (ids: number[]) => {
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }
    if (typeof (globalThis as unknown as { WebSocket?: unknown }).WebSocket === 'undefined') {
      ;(globalThis as unknown as Record<string, unknown>).WebSocket = function () {
        /* noop */
      }
    }

    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
      ],
    })
    await router.push('/books')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    store.audiobooks = ids.map((id) => ({
      id,
      title: `Book ${id}`,
      authors: ['Author'],
      files: [],
    })) as unknown as import('@/types').Audiobook[]
    store.fetchLibrary = vi.fn(async () => undefined)
    ids.forEach((id) => store.selectedIds.add(id))

    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))
    return wrapper
  }

  const convertButton = (wrapper: ReturnType<typeof mount>) =>
    wrapper.findAll('button.toolbar-btn').find((b) => b.text().includes('Convert Selected'))

  beforeEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
    showConfirmMock.mockResolvedValue(true)
    const pinia = createPinia()
    setActivePinia(pinia)
  })

  it('offers the action only once books are selected', async () => {
    const empty = await mountWithSelection([])
    expect(convertButton(empty)).toBeUndefined()

    const selected = await mountWithSelection([1, 2])
    expect(convertButton(selected)).toBeDefined()
  })

  it('confirms before queueing, because converting rewrites the library', async () => {
    showConfirmMock.mockResolvedValue(false)
    const wrapper = await mountWithSelection([1, 2])

    await convertButton(wrapper)!.trigger('click')
    await new Promise((r) => setTimeout(r, 0))

    expect(showConfirmMock).toHaveBeenCalled()
    expect(convertAudiobooksBulkMock).not.toHaveBeenCalled()
  })

  it('sends every selected id in one request, not one request per book', async () => {
    convertAudiobooksBulkMock.mockResolvedValue({
      requestedCount: 3,
      queuedCount: 3,
      results: [1, 2, 3].map((audiobookId) => ({ audiobookId, outcome: 'Queued' })),
    })
    const wrapper = await mountWithSelection([1, 2, 3])

    await convertButton(wrapper)!.trigger('click')
    await new Promise((r) => setTimeout(r, 0))

    expect(convertAudiobooksBulkMock).toHaveBeenCalledTimes(1)
    expect(convertAudiobooksBulkMock).toHaveBeenCalledWith([1, 2, 3])
    expect(successToast).toHaveBeenCalled()
    expect((successToast.mock.calls[0] as [string, string])[1]).toContain('3 books queued')
  })

  it('summarises skipped books by reason rather than one message each', async () => {
    convertAudiobooksBulkMock.mockResolvedValue({
      requestedCount: 4,
      queuedCount: 1,
      results: [
        { audiobookId: 1, outcome: 'Queued' },
        { audiobookId: 2, outcome: 'NothingToConvert' },
        { audiobookId: 3, outcome: 'NothingToConvert' },
        { audiobookId: 4, outcome: 'AlreadyQueued' },
      ],
    })
    const wrapper = await mountWithSelection([1, 2, 3, 4])

    await convertButton(wrapper)!.trigger('click')
    await new Promise((r) => setTimeout(r, 0))

    expect(successToast).toHaveBeenCalledTimes(1)
    const [, message] = successToast.mock.calls[0] as [string, string]
    expect(message).toContain('1 of 4 queued')
    expect(message).toContain('2 nothing to convert')
    expect(message).toContain('1 already queued')
  })

  it('warns rather than claiming success when nothing could be queued', async () => {
    convertAudiobooksBulkMock.mockResolvedValue({
      requestedCount: 2,
      queuedCount: 0,
      results: [
        { audiobookId: 1, outcome: 'EncoderUnavailable' },
        { audiobookId: 2, outcome: 'EncoderUnavailable' },
      ],
    })
    const wrapper = await mountWithSelection([1, 2])

    await convertButton(wrapper)!.trigger('click')
    await new Promise((r) => setTimeout(r, 0))

    expect(successToast).not.toHaveBeenCalled()
    expect(warningToast).toHaveBeenCalled()
    const [, message] = warningToast.mock.calls[0] as [string, string]
    expect(message).toContain('2 no encoder installed')
  })

  it('keeps the selection when nothing was queued, so it can be retried', async () => {
    convertAudiobooksBulkMock.mockResolvedValue({
      requestedCount: 2,
      queuedCount: 0,
      results: [
        { audiobookId: 1, outcome: 'EncoderUnavailable' },
        { audiobookId: 2, outcome: 'EncoderUnavailable' },
      ],
    })
    const wrapper = await mountWithSelection([1, 2])

    await convertButton(wrapper)!.trigger('click')
    await new Promise((r) => setTimeout(r, 0))

    expect(useLibraryStore().selectedIds.size).toBe(2)
  })
})

describe('AudiobooksView Filter Storage', () => {
  const CUSTOM_FILTERS_KEY = 'listenarr.customFilters'
  const SELECTED_FILTER_KEY = 'listenarr.selectedFilter'
  const aFilter = (id: string, label: string) => ({ id, label, rules: [] })

  const mountView = async () => {
    if (
      typeof (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined'
    ) {
      ;(globalThis as unknown as Record<string, unknown>).ResizeObserver = class {
        observe() {}
        disconnect() {}
      }
    }
    if (typeof (globalThis as unknown as { WebSocket?: unknown }).WebSocket === 'undefined') {
      ;(globalThis as unknown as Record<string, unknown>).WebSocket = function () {
        /* noop */
      }
    }

    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div />' } },
        {
          path: '/books',
          name: 'books',
          component: AudiobooksView,
          meta: { libraryGroup: 'books' },
        },
      ],
    })
    await router.push('/books')
    await router.isReady().catch(() => {})

    const store = useLibraryStore()
    store.audiobooks = [] as unknown as import('@/types').Audiobook[]
    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobooksView, {
      global: {
        plugins: [pinia, router],
        stubs: [
          'BulkEditModal',
          'EditAudiobookModal',
          'CustomFilterModal',
          'FiltersDropdown',
          'ViewOptionsDropdown',
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))
    return wrapper
  }

  const filtersOf = (wrapper: ReturnType<typeof mount>) =>
    (wrapper.vm as unknown as { customFilters: Array<{ id: string }> }).customFilters

  beforeEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
    saveLibraryCustomFiltersMock.mockResolvedValue({ saved: true })
    getApplicationSettingsMock.mockResolvedValue({})
    const pinia = createPinia()
    setActivePinia(pinia)
  })

  it('reads its filters from the server, so every machine shows the same ones', async () => {
    getApplicationSettingsMock.mockResolvedValue({
      libraryCustomFiltersJson: JSON.stringify([aFilter('srv-1', 'Shared')]),
    })

    const wrapper = await mountView()

    expect(filtersOf(wrapper).map((f) => f.id)).toEqual(['srv-1'])
  })

  it('adopts filters left in the browser by an older version and hands them to the server', async () => {
    localStorage.setItem(CUSTOM_FILTERS_KEY, JSON.stringify([aFilter('local-1', 'Mine')]))

    const wrapper = await mountView()

    expect(saveLibraryCustomFiltersMock).toHaveBeenCalledWith([aFilter('local-1', 'Mine')])
    expect(filtersOf(wrapper).map((f) => f.id)).toEqual(['local-1'])
    // Once the server holds them the browser copy is only a source of stale duplicates.
    expect(localStorage.getItem(CUSTOM_FILTERS_KEY)).toBeNull()
  })

  it("does not let a second machine's stale copy overwrite the shared set", async () => {
    getApplicationSettingsMock.mockResolvedValue({
      libraryCustomFiltersJson: JSON.stringify([aFilter('srv-1', 'Shared')]),
    })
    localStorage.setItem(CUSTOM_FILTERS_KEY, JSON.stringify([aFilter('stale-1', 'Old')]))

    const wrapper = await mountView()

    expect(saveLibraryCustomFiltersMock).not.toHaveBeenCalled()
    expect(filtersOf(wrapper).map((f) => f.id)).toEqual(['srv-1'])
    expect(localStorage.getItem(CUSTOM_FILTERS_KEY)).toBeNull()
  })

  it('keeps the browser copy when the migration cannot reach the server', async () => {
    localStorage.setItem(CUSTOM_FILTERS_KEY, JSON.stringify([aFilter('local-1', 'Mine')]))
    saveLibraryCustomFiltersMock.mockRejectedValue(new Error('offline'))

    const wrapper = await mountView()

    // It is the only copy that exists; the next load retries.
    expect(localStorage.getItem(CUSTOM_FILTERS_KEY)).not.toBeNull()
    expect(filtersOf(wrapper).map((f) => f.id)).toEqual(['local-1'])
  })

  it('honours a stored selection naming a filter the server returned', async () => {
    getApplicationSettingsMock.mockResolvedValue({
      libraryCustomFiltersJson: JSON.stringify([aFilter('srv-1', 'Shared')]),
    })
    localStorage.setItem(SELECTED_FILTER_KEY, 'srv-1')

    const wrapper = await mountView()

    expect((wrapper.vm as unknown as { selectedFilterId: string | null }).selectedFilterId).toBe(
      'srv-1',
    )
  })

  it('survives a filters column that is empty or not valid JSON', async () => {
    getApplicationSettingsMock.mockResolvedValue({ libraryCustomFiltersJson: '' })
    expect(filtersOf(await mountView())).toEqual([])

    getApplicationSettingsMock.mockResolvedValue({ libraryCustomFiltersJson: 'not json' })
    expect(filtersOf(await mountView())).toEqual([])

    getApplicationSettingsMock.mockResolvedValue({ libraryCustomFiltersJson: '{"not":"array"}' })
    expect(filtersOf(await mountView())).toEqual([])
  })
})

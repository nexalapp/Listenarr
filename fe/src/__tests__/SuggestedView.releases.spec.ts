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
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import type { SuggestedBook, SuggestionSnapshot } from '@/types'

function book(asin: string, release?: SuggestedBook['release']): SuggestedBook {
  return { asin, title: asin, authors: ['Author'], narrators: [], genres: [], release } as SuggestedBook
}

const checkedAt = '2026-10-24T00:00:00Z'
const snapshot: SuggestionSnapshot = {
  authors: [],
  series: [
    {
      series: 'Expeditionary Force',
      libraryCount: 3,
      monitored: false,
      missing: [
        book('FOUND', { found: true, indexer: 'Abook.link', title: 'Armageddon', checkedAt }),
        book('MISSING', { found: false, checkedAt }),
        book('UNCHECKED'),
      ],
    },
    {
      series: 'Nothing Held',
      libraryCount: 1,
      monitored: false,
      missing: [book('ALSO-MISSING', { found: false, checkedAt })],
    },
  ],
  relatedAuthors: [],
  coverage: { authorsInLibrary: 0, authorsWithCatalog: 0, seriesInLibrary: 2, seriesWithCatalog: 2 },
  ignored: [],
  releases: { total: 4, checked: 3, found: 1 },
}

vi.mock('@/services/api', () => ({
  apiService: {
    getSuggestions: vi.fn(async () => snapshot),
    getSuggestionRefreshStatus: vi.fn(async () => ({
      running: false,
      completed: 0,
      total: 0,
      failed: 0,
    })),
    refreshSuggestions: vi.fn(),
    ignoreSuggestion: vi.fn(),
    restoreSuggestion: vi.fn(),
    addToLibrary: vi.fn(),
    getFoundBooks: vi.fn(async () => ({ items: [], scanning: false })),
    getFoundWatchFolders: vi.fn(async () => []),
  },
}))

vi.mock('@/services/signalr', () => ({
  signalRService: {
    onFoundBooksChanged: vi.fn(() => () => {}),
    onAudiobookUpdate: vi.fn(() => () => {}),
    onScanJobUpdate: vi.fn(() => () => {}),
  },
}))

vi.mock('@/services/toastService', () => ({
  useToast: () => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }),
}))

import SuggestedView from '@/views/content/SuggestedView.vue'

async function mountSeries() {
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/suggested/:tab?', name: 'suggested', component: { template: '<div />' } },
      { path: '/collection/:type/:name', name: 'collection', component: { template: '<div />' } },
    ],
  })
  await router.push('/suggested/series')
  await router.isReady()
  const wrapper = mount(SuggestedView, {
    global: {
      plugins: [router],
      stubs: {
        FoundBooksTab: true,
        AddLibraryModal: true,
        ManualSearchModal: true,
        SuggestedBookCard: { props: ['book'], template: '<div class="card">{{ book.asin }}</div>' },
      },
    },
  })
  await flushPromises()
  return wrapper
}

describe('SuggestedView shows what can be downloaded', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('lists only books an indexer holds a release of, and drops groups left empty', async () => {
    const wrapper = await mountSeries()

    expect(wrapper.findAll('.card').map((card) => card.text())).toEqual(['FOUND'])
    expect(wrapper.text()).not.toContain('Nothing Held')
    expect(wrapper.find('.tab.active').text()).toContain('1')
    expect(wrapper.find('.release-coverage').text()).toContain('Checked 3 of 4')
  })

  it('shows the rest, unchecked ones included, when asked', async () => {
    const wrapper = await mountSeries()

    await wrapper.find('.show-unavailable input').setValue(true)

    expect(wrapper.findAll('.card').map((card) => card.text())).toEqual([
      'FOUND',
      'MISSING',
      'UNCHECKED',
      'ALSO-MISSING',
    ])
    expect(localStorage.getItem('listenarr.suggested.showUnavailable')).toBe('1')
  })
})

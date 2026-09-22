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
import type { SuggestionSnapshot } from '@/types'

const emptySnapshot: SuggestionSnapshot = {
  authors: [],
  series: [],
  relatedAuthors: [],
  coverage: {
    authorsInLibrary: 0,
    authorsWithCatalog: 0,
    seriesInLibrary: 0,
    seriesWithCatalog: 0,
  },
  ignored: [],
}

vi.mock('@/services/api', () => ({
  apiService: {
    getSuggestions: vi.fn(async () => emptySnapshot),
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

function buildRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: { template: '<div />' } },
      {
        path: '/suggested/:tab(authors|series|related|found)?',
        name: 'suggested',
        component: SuggestedView,
        meta: { keepAcrossParams: true },
      },
    ],
  })
}

async function mountAt(router: Router, path: string) {
  await router.push(path)
  await router.isReady()
  const wrapper = mount(SuggestedView, {
    global: {
      plugins: [router],
      stubs: {
        FoundBooksTab: { template: '<div class="found-tab-stub" />' },
        AddLibraryModal: true,
        ManualSearchModal: true,
        SuggestedBookCard: true,
      },
    },
  })
  await flushPromises()
  return wrapper
}

const activeTabLabel = (wrapper: ReturnType<typeof mount>) => wrapper.find('.tab.active').text()

describe('SuggestedView tabs in the path', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('opens the tab the path names, and the first tab for the bare path', async () => {
    const router = buildRouter()
    const wrapper = await mountAt(router, '/suggested/series')
    expect(activeTabLabel(wrapper)).toContain('From your series')

    await router.push('/suggested')
    await flushPromises()
    expect(activeTabLabel(wrapper)).toContain('From your authors')
  })

  it('pushes the tab into the path, so Back returns to the tab that was open', async () => {
    const router = buildRouter()
    const wrapper = await mountAt(router, '/suggested')

    const tabs = wrapper.findAll('.tab')
    await tabs.find((tab) => tab.text().includes('Found on disk'))!.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/suggested/found')
    expect(wrapper.find('.found-tab-stub').exists()).toBe(true)

    router.back()
    await flushPromises()
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(router.currentRoute.value.fullPath).toBe('/suggested')
    expect(activeTabLabel(wrapper)).toContain('From your authors')
  })

  it('does not add a history entry for the tab already open', async () => {
    const router = buildRouter()
    const wrapper = await mountAt(router, '/suggested/series')
    const push = vi.spyOn(router, 'push')

    await wrapper
      .findAll('.tab')
      .find((tab) => tab.text().includes('From your series'))!
      .trigger('click')

    expect(push).not.toHaveBeenCalled()
  })
})

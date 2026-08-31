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
import { describe, it, beforeEach, afterEach, expect, vi } from 'vitest'
import CalendarView from '@/views/content/CalendarView.vue'
import { useLibraryStore } from '@/stores/library'

const getMonitoredSeries = vi.fn(async () => [] as unknown[])
const getMonitoredAuthors = vi.fn(async () => [] as unknown[])
const updateAudiobook = vi.fn(async () => ({ message: 'ok' }))
const searchAndDownload = vi.fn(async () => ({ success: true, indexerUsed: 'Test Indexer' }))

vi.mock('@/services/api', () => ({
  apiService: {
    getImageUrl: vi.fn((url: string) => url),
    getQualityProfiles: vi.fn(async () => []),
    getMonitoredSeries: (...args: unknown[]) => getMonitoredSeries(...(args as [])),
    getMonitoredAuthors: (...args: unknown[]) => getMonitoredAuthors(...(args as [])),
    updateAudiobook: (...args: unknown[]) => updateAudiobook(...(args as [])),
    searchAndDownload: (...args: unknown[]) => searchAndDownload(...(args as [])),
  },
  getImageUrl: vi.fn((url: string) => url),
  ensureImageCached: vi.fn(async () => true),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
}))

// Mid-August 2026, so the month on screen is August 2026 and dates later that month
// are still ahead.
const TODAY = new Date(2026, 7, 15, 12, 0, 0)

type LibraryBooks = ReturnType<typeof useLibraryStore>['audiobooks']

const mountCalendar = async (books: unknown[]) => {
  const pinia = createPinia()
  setActivePinia(pinia)

  const store = useLibraryStore()
  store.audiobooks = books as LibraryBooks
  store.fetchLibrary = vi.fn(async () => undefined)

  const wrapper = mount(CalendarView, { global: { plugins: [pinia] } })
  await new Promise((resolve) => setTimeout(resolve, 0))
  await wrapper.vm.$nextTick()
  return wrapper
}

describe('CalendarView release states', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.setSystemTime(TODAY)
    window.localStorage.clear()
    getMonitoredSeries.mockResolvedValue([])
    getMonitoredAuthors.mockResolvedValue([])
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('tells an announced book apart from a wanted one and from one already in the library', async () => {
    const wrapper = await mountCalendar([
      {
        id: 1,
        title: 'Announced Book',
        publishedDate: '2026-08-20',
        status: 'announced',
        monitored: true,
        wanted: true,
      },
      {
        id: 2,
        title: 'Wanted Book',
        publishedDate: '2026-08-21',
        status: 'no-file',
        monitored: true,
        wanted: true,
      },
      {
        id: 3,
        title: 'Owned Book',
        publishedDate: '2026-08-22',
        status: 'quality-match',
        monitored: true,
        wanted: false,
      },
    ])

    const badges = wrapper.findAll('.status-badge').map((badge) => badge.text())
    expect(badges).toContain('Announced')
    expect(badges).toContain('Wanted')
    expect(badges).toContain('In library')

    // The three states are also distinguishable in the grid, which is too dense for text.
    expect(wrapper.find('.calendar-item.status-announced').exists()).toBe(true)
    expect(wrapper.find('.calendar-item.status-no-file').exists()).toBe(true)
    expect(wrapper.find('.calendar-item.status-quality-match').exists()).toBe(true)
  })

  it('shows a month-only date as a month rather than dropping it or inventing a day', async () => {
    const wrapper = await mountCalendar([
      {
        id: 10,
        title: 'Month Only Book',
        publishedDate: '2026-08',
        status: 'announced',
        monitored: true,
      },
    ])

    const panel = wrapper.find('.imprecise-panel')
    expect(panel.exists()).toBe(true)
    expect(panel.text()).toContain('Sometime in August 2026')
    expect(panel.text()).toContain('Month Only Book')
    expect(panel.text()).toContain('Aug 2026')

    // It never occupies a day square, because no day was ever announced.
    expect(wrapper.find('.calendar-item').exists()).toBe(false)
  })

  it('shows a year-only date as a year', async () => {
    const wrapper = await mountCalendar([
      {
        id: 11,
        title: 'Year Only Book',
        publishedDate: '2026',
        status: 'announced',
        monitored: true,
      },
    ])

    const panel = wrapper.find('.imprecise-panel')
    expect(panel.text()).toContain('Sometime in 2026')
    expect(panel.text()).toContain('Year Only Book')
    expect(wrapper.find('.calendar-item').exists()).toBe(false)
  })

  it('renders the sidebar date at the precision it was given', async () => {
    const wrapper = await mountCalendar([
      { id: 20, title: 'Precise', publishedDate: '2026-08-20', status: 'announced' },
      { id: 21, title: 'Vague', publishedDate: '2026-09', status: 'announced' },
    ])

    const dates = wrapper.findAll('.upcoming-date').map((node) => node.text())
    expect(dates).toContain('Aug 20, 2026')
    expect(dates).toContain('Sep 2026')
  })

  it('says a monitor is failing instead of looking like a quiet calendar', async () => {
    getMonitoredSeries.mockResolvedValue([
      {
        id: 5,
        seriesName: 'A Carrick Hall Novel',
        region: 'us',
        language: 'english',
        createdAt: '',
        updatedAt: '',
        lastError: 'Series catalog could not be loaded.',
      },
    ])

    const wrapper = await mountCalendar([])

    const alert = wrapper.find('.monitor-alert')
    expect(alert.exists()).toBe(true)
    expect(alert.text()).toContain('1 monitor is failing')
    expect(alert.text()).toContain('A Carrick Hall Novel')
    expect(alert.text()).toContain('Series catalog could not be loaded.')

    // And the empty list stops claiming there is simply nothing coming.
    expect(wrapper.find('.empty-message').text()).toContain('monitors are failing')
  })

  it('says nothing is announced when the monitors are healthy', async () => {
    getMonitoredSeries.mockResolvedValue([
      {
        id: 5,
        seriesName: 'Healthy Series',
        region: 'us',
        language: 'english',
        createdAt: '',
        updatedAt: '',
        lastSuccessfulSyncAt: '2026-08-15T00:00:00Z',
      },
    ])

    const wrapper = await mountCalendar([])

    expect(wrapper.find('.monitor-alert').exists()).toBe(false)
    expect(wrapper.find('.empty-message').text()).toContain('Nothing announced')
  })

  it('invites the user to follow something when nothing is monitored at all', async () => {
    const wrapper = await mountCalendar([])

    expect(wrapper.find('.empty-message').text()).toContain('No monitored series or authors yet')
  })

  it('offers the same actions on an imprecisely-dated row', async () => {
    // A month-only announcement is the row a user most wants to act on, so it cannot
    // be the one row that is display-only.
    const wrapper = await mountCalendar([
      {
        id: 40,
        title: 'Month Only Announcement',
        publishedDate: '2026-08',
        status: 'announced',
        monitored: false,
        wanted: false,
      },
    ])

    const buttons = wrapper.findAll('.imprecise-item .row-action')
    expect(buttons.length).toBe(2)

    await buttons[0].trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(updateAudiobook).toHaveBeenCalledWith(40, { monitored: true })
  })

  it('marks a row wanted without leaving the view', async () => {
    const wrapper = await mountCalendar([
      {
        id: 30,
        title: 'Unmonitored Announcement',
        publishedDate: '2026-08-20',
        status: 'announced',
        monitored: false,
        wanted: false,
      },
    ])

    const wantButton = wrapper.find('.upcoming-actions .row-action')
    expect(wantButton.attributes('title')).toBe('Mark wanted')

    await wantButton.trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(updateAudiobook).toHaveBeenCalledWith(30, { monitored: true })
  })

  it('searches a row without leaving the view', async () => {
    const wrapper = await mountCalendar([
      {
        id: 31,
        title: 'Monitored Announcement',
        publishedDate: '2026-08-20',
        status: 'announced',
        monitored: true,
        wanted: true,
      },
    ])

    // Already monitored, so the only action offered is the search.
    const buttons = wrapper.findAll('.upcoming-actions .row-action')
    expect(buttons).toHaveLength(1)
    expect(buttons[0].attributes('title')).toBe('Search now')

    await buttons[0].trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(searchAndDownload).toHaveBeenCalledWith(31)
  })
})

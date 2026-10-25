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
import type { DiscoverFeed, SuggestedBook } from '@/types'

const idle = { running: false, completed: 0, total: 0, failed: 0 }

function book(title: string, asin: string): SuggestedBook {
  return {
    asin,
    title,
    authors: ['James S. A. Corey'],
    narrators: [],
    genres: [],
  } as unknown as SuggestedBook
}

function rated(title: string, asin: string, rating?: number, count?: number): SuggestedBook {
  return { ...book(title, asin), ratingOverall: rating, ratingCount: count } as SuggestedBook
}

/** Two chart categories that share a book, which is what ungrouped has to handle. */
function twoSections(): DiscoverFeed {
  return feed({
    sections: [
      {
        sectionId: '1',
        sectionName: 'Science Fiction & Fantasy',
        pagesFetched: 1,
        held: 0,
        books: [
          { rank: 2, resolved: true, book: rated('Shared', 'B1', 4.9, 3) },
          { rank: 5, resolved: true, book: rated('Only SF', 'B2', 4.5, 9000) },
        ],
      },
      {
        sectionId: '2',
        sectionName: 'Literature & Fiction',
        pagesFetched: 1,
        held: 1,
        books: [
          { rank: 9, resolved: true, book: rated('Shared', 'B1', 4.9, 3) },
          { rank: 11, resolved: true, book: rated('Only Lit', 'B3', 4.7, 400) },
        ],
      },
    ],
  })
}

function feed(overrides: Partial<DiscoverFeed> = {}): DiscoverFeed {
  return {
    source: 'audible-charts',
    pagesPerSection: 5,
    available: true,
    status: idle,
    sections: [
      {
        sectionId: '18580606011',
        sectionName: 'Science Fiction & Fantasy',
        pagesFetched: 1,
        totalItems: 100,
        held: 2,
        books: [
          { rank: 3, resolved: true, book: book('Drive', 'B1') },
          { rank: 7, resolved: true, book: book('Nemesis Games', 'B2') },
        ],
      },
    ],
    ...overrides,
  } as DiscoverFeed
}

// Hoisted: vi.mock's factory runs before the module body, so the stub it returns
// cannot be a plain const declared here.
const api = vi.hoisted(() => ({
  getDiscover: vi.fn(),
  getDiscoverRefreshStatus: vi.fn(),
  refreshDiscover: vi.fn(),
  loadMoreDiscover: vi.fn(),
  getDiscoverSections: vi.fn(),
  saveDiscoverSections: vi.fn(),
  ignoreSuggestion: vi.fn(),
  getProtectedImageUrl: vi.fn(),
}))

vi.mock('@/services/api', () => ({ apiService: api }))

vi.mock('@/services/toastService', () => ({
  useToast: () => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }),
}))

import DiscoverTab from '@/components/domain/audiobook/DiscoverTab.vue'

function mountTab(source: 'charts' | 'abook' = 'charts') {
  return mount(DiscoverTab, {
    props: { source, addedKeys: new Set<string>(), addingKeys: new Set<string>() },
    global: {
      stubs: {
        SuggestedBookCard: {
          props: ['book', 'rank', 'note', 'noteUrl'],
          emits: ['ignore'],
          template:
            '<div class="card-stub" :data-rank="rank" :data-note="note">{{ book.title }}' +
            '<button class="ignore-stub" @click="$emit(\'ignore\')" /></div>',
        },
        DiscoverSectionPicker: { template: '<div class="picker-stub" />' },
      },
    },
  })
}

describe('DiscoverTab', () => {
  beforeEach(() => {
    // Sort and grouping are remembered per browser, so one test's choice would
    // otherwise be the next test's starting point.
    localStorage.clear()
    vi.clearAllMocks()
    api.getDiscover.mockImplementation(async () => feed())
    api.getDiscoverRefreshStatus.mockImplementation(async () => idle)
    api.refreshDiscover.mockImplementation(async () => idle)
    api.loadMoreDiscover.mockImplementation(async () => idle)
    api.getDiscoverSections.mockImplementation(async () => ({
      source: 'audible-charts',
      options: [],
      selected: [],
      pagesPerSection: 5,
      available: true,
    }))
    api.ignoreSuggestion.mockImplementation(async () => ({ key: 'asin:B1' }))
  })

  it('shows each followed section with what it is still missing', async () => {
    const wrapper = mountTab()
    await flushPromises()

    expect(wrapper.text()).toContain('Science Fiction & Fantasy')
    expect(wrapper.findAll('.card-stub')).toHaveLength(2)
  })

  it('says how many of a section the library already has, rather than listing them', async () => {
    const wrapper = mountTab()
    await flushPromises()

    expect(wrapper.text()).toContain('2 already yours')
  })

  it("keeps a book's place in the chart on its card", async () => {
    const wrapper = mountTab()
    await flushPromises()

    // Third in the chart stays third even though the two above it are held.
    expect(wrapper.findAll('.card-stub')[0]!.attributes('data-rank')).toBe('3')
  })

  it('does not number a forum listing, which has no ranking', async () => {
    api.getDiscover.mockImplementation(async () =>
      feed({
        source: 'abook-link',
        sections: [
          {
            sectionId: '7',
            sectionName: 'Science Fiction',
            pagesFetched: 1,
            held: 0,
            books: [
              {
                rank: 1,
                resolved: false,
                postedAt: new Date().toISOString(),
                sourceNote: 'Jack Campbell - Lost Fleet 04 - Valiant (2022)',
                sourceUrl: 'https://abook.link/book/index.php?topic=123463',
                book: book('Valiant', 'B9'),
              },
            ],
          },
        ],
      }),
    )

    const wrapper = mountTab('abook')
    await flushPromises()

    const card = wrapper.find('.card-stub')
    expect(card.attributes('data-rank')).toBeUndefined()
    expect(card.attributes('data-note')).toContain('not looked up yet')
    expect(card.attributes('data-note')).toContain('Lost Fleet 04')
  })

  it('offers Load more only while a section is not as deep as the setting allows', async () => {
    const wrapper = mountTab()
    await flushPromises()
    const loadMore = () =>
      wrapper.findAll('button').find((button) => button.text().includes('Load more'))!

    expect(loadMore().attributes('disabled')).toBeUndefined()

    api.getDiscover.mockImplementation(async () =>
      feed({ pagesPerSection: 1, sections: feed().sections }),
    )
    const deep = mountTab()
    await flushPromises()
    expect(
      deep
        .findAll('button')
        .find((button) => button.text().includes('Load more'))!
        .attributes('disabled'),
    ).toBeDefined()
  })

  it('lists a book once when the sections are turned off, keeping its best place', async () => {
    api.getDiscover.mockImplementation(async () => twoSections())
    const wrapper = mountTab()
    await flushPromises()
    expect(wrapper.findAll('.card-stub')).toHaveLength(4)

    await wrapper.find('.view-control input[type="checkbox"]').setValue(false)

    // One block, three books: the shared one is not offered twice.
    const titles = wrapper.findAll('.card-stub').map((card) => card.text())
    expect(titles).toHaveLength(3)
    expect(titles.filter((title) => title.includes('Shared'))).toHaveLength(1)
    // Second in one chart and ninth in the other: it keeps the better place,
    // which now rides on the note rather than the badge.
    expect(wrapper.findAll('.card-stub')[0]!.attributes('data-note')).toContain('#2 in')
  })

  it('says on the card which sections a book came from once they are off', async () => {
    api.getDiscover.mockImplementation(async () => twoSections())
    const wrapper = mountTab()
    await flushPromises()
    await wrapper.find('.view-control input[type="checkbox"]').setValue(false)

    const shared = wrapper.findAll('.card-stub').find((card) => card.text().includes('Shared'))!
    // The place belongs to the chart it was read from, and the badge is gone:
    // two cards both badged "#1" from different categories read as a bug.
    expect(shared.attributes('data-rank')).toBeUndefined()
    expect(shared.attributes('data-note')).toContain('#2 in Science Fiction & Fantasy')
    expect(shared.attributes('data-note')).toContain('also in Literature & Fiction')
  })

  it('counts how many books were in more than one section', async () => {
    api.getDiscover.mockImplementation(async () => twoSections())
    const wrapper = mountTab()
    await flushPromises()
    await wrapper.find('.view-control input[type="checkbox"]').setValue(false)

    expect(wrapper.text()).toContain('1 book was in more than one category')
  })

  it('does not let a handful of five-star ratings outrank a thousand good ones', async () => {
    api.getDiscover.mockImplementation(async () => twoSections())
    const wrapper = mountTab()
    await flushPromises()
    await wrapper.find('.view-control input[type="checkbox"]').setValue(false)
    await wrapper.find('.view-select').setValue('rating')

    // 4.9 from three people, 4.7 from four hundred, 4.5 from nine thousand.
    const titles = wrapper.findAll('.card-stub').map((card) => card.text())
    expect(titles).toEqual(['Only Lit', 'Only SF', 'Shared'])
  })

  it('sorts by title across every section when asked', async () => {
    api.getDiscover.mockImplementation(async () => twoSections())
    const wrapper = mountTab()
    await flushPromises()
    await wrapper.find('.view-control input[type="checkbox"]').setValue(false)
    await wrapper.find('.view-select').setValue('title')

    expect(wrapper.findAll('.card-stub').map((card) => card.text())).toEqual([
      'Only Lit',
      'Only SF',
      'Shared',
    ])
  })

  it('keeps the chart order when nothing else is asked for', async () => {
    api.getDiscover.mockImplementation(async () => twoSections())
    const wrapper = mountTab()
    await flushPromises()
    await wrapper.find('.view-control input[type="checkbox"]').setValue(false)

    expect(wrapper.findAll('.card-stub').map((card) => card.text())).toEqual([
      'Shared',
      'Only SF',
      'Only Lit',
    ])
  })

  it('puts the newest post first across boards, not one board after another', async () => {
    const posted = (title: string, asin: string, iso: string, rank: number) => ({
      rank,
      resolved: true,
      postedAt: iso,
      book: book(title, asin),
    })
    api.getDiscover.mockImplementation(async () =>
      feed({
        source: 'abook-link',
        sections: [
          {
            sectionId: '7',
            sectionName: 'Science Fiction',
            pagesFetched: 1,
            held: 0,
            books: [posted('Yesterday SF', 'B1', '2026-10-23T09:00:00Z', 1)],
          },
          {
            sectionId: '8',
            sectionName: 'Horror',
            pagesFetched: 1,
            held: 0,
            books: [posted('This morning Horror', 'B2', '2026-10-24T08:00:00Z', 1)],
          },
        ],
      }),
    )

    const wrapper = mountTab('abook')
    await flushPromises()
    await wrapper.find('.view-control input[type="checkbox"]').setValue(false)

    // Concatenating the boards would bury this morning's post under yesterday's.
    expect(wrapper.findAll('.card-stub').map((card) => card.text())).toEqual([
      'This morning Horror',
      'Yesterday SF',
    ])
  })

  it('explains itself rather than looking empty when the source cannot be used', async () => {
    api.getDiscover.mockImplementation(async () =>
      feed({
        available: false,
        unavailable: 'No abook.link source is configured.',
        sections: [],
      }),
    )

    const wrapper = mountTab('abook')
    await flushPromises()

    expect(wrapper.text()).toContain('No abook.link source is configured.')
  })

  it('takes an ignored book off the list at once', async () => {
    const wrapper = mountTab()
    await flushPromises()
    expect(wrapper.findAll('.card-stub')).toHaveLength(2)

    await wrapper.findAll('.ignore-stub')[0]!.trigger('click')
    await flushPromises()

    expect(api.ignoreSuggestion).toHaveBeenCalledWith(
      expect.objectContaining({ asin: 'B1', title: 'Drive' }),
    )
    expect(wrapper.findAll('.card-stub')).toHaveLength(1)
  })
})

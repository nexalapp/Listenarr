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
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import type { LibraryTagRow, LibraryTagTable } from '@/types'

const getLibraryTags = vi.fn()
const push = vi.fn()

vi.mock('@/services/api', () => ({
  apiService: {
    getLibraryTags: (...args: unknown[]) => getLibraryTags(...args),
  },
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

const row = (overrides: Partial<LibraryTagRow> = {}): LibraryTagRow => ({
  audiobookId: 7,
  fileId: 1,
  bookTitle: 'Drive',
  fileName: 'Corey - Drive.m4b',
  path: 'Corey - Drive.m4b',
  extension: 'm4b',
  writable: true,
  tags: { title: 'Drive', album: 'Drive' },
  expected: { title: 'Drive', album: '[The Expanse 2.7] Drive' },
  mismatched: ['album'],
  error: null,
  ...overrides,
})

const table = (rows: LibraryTagRow[]): LibraryTagTable => ({
  generatedAt: '2026-09-01T00:00:00Z',
  filesRead: rows.length,
  columns: [
    { tag: 'title', label: 'Title', isLongText: false },
    { tag: 'album', label: 'Album', isLongText: false },
    { tag: 'description', label: 'Description', isLongText: true },
  ],
  rows,
})

async function mountView(result: LibraryTagTable = table([row()])) {
  getLibraryTags.mockResolvedValue(result)
  const { default: TagsView } = await import('@/views/library/TagsView.vue')

  const wrapper = mount(TagsView)
  await new Promise((resolve) => setTimeout(resolve, 0))
  await wrapper.vm.$nextTick()
  await wrapper.vm.$nextTick()
  return wrapper
}

describe('TagsView', () => {
  beforeEach(() => {
    getLibraryTags.mockReset()
    push.mockReset()
    localStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows one row per file, with the value the file carries', async () => {
    const wrapper = await mountView(
      table([
        row({ fileId: 1, fileName: 'Part 1.m4b' }),
        row({ fileId: 2, fileName: 'Part 2.m4b' }),
      ]),
    )

    const rows = wrapper.findAll('.tags-row')
    expect(rows).toHaveLength(2)
    expect(rows[0].text()).toContain('Part 1.m4b')
    expect(rows[1].text()).toContain('Part 2.m4b')
  })

  it('opens on every tag Listenarr can write, description first', async () => {
    // The table answers "are these right?", and a default that showed a third of the
    // tags could not answer it. The description leads because it is the tag this whole
    // path exists for.
    const wrapper = await mountView()

    const headers = wrapper.findAll('.tags-th-label').map((header) => header.text())
    expect(headers).toEqual(['Filename', 'Description', 'Title', 'Album'])
  })

  it('remembers a narrowed set of columns instead of reopening on all of them', async () => {
    localStorage.setItem('listenarr.tagsView.columns.v2', JSON.stringify(['title']))

    const wrapper = await mountView()

    expect(wrapper.findAll('.tags-th-label').map((header) => header.text())).toEqual([
      'Filename',
      'Title',
    ])
  })

  it('marks the cells a tag write would change', async () => {
    // The point of the table: spotting the handful of files that are wrong without
    // reading every value in it.
    const wrapper = await mountView()

    const changed = wrapper.findAll('.tags-td--mismatch')
    expect(changed).toHaveLength(1)
    expect(changed[0].text()).toBe('Drive')
    expect(changed[0].attributes('title')).toContain('[The Expanse 2.7] Drive')
  })

  it('marks a file that is not an M4B rather than hiding it', async () => {
    // Which books still carry ID3 is one of the things the table is opened to answer.
    const wrapper = await mountView(
      table([row({ writable: false, extension: 'mp3', fileName: 'Chapter 1.mp3' })]),
    )

    expect(wrapper.find('.tags-row--unwritable').exists()).toBe(true)
    expect(wrapper.text()).toContain('Chapter 1.mp3')
  })

  it('filters on values in columns that are not shown', async () => {
    // Hiding a column is a display choice; it should not quietly remove rows from a
    // search.
    localStorage.setItem('listenarr.tagsView.columns.v2', JSON.stringify(['title']))

    const wrapper = await mountView(
      table([
        row({ fileId: 1, fileName: 'A.m4b', tags: { title: 'A', description: 'a lunar heist' } }),
        row({ fileId: 2, fileName: 'B.m4b', tags: { title: 'B', description: 'a desert war' } }),
      ]),
    )

    await wrapper.find('.search-input').setValue('lunar')
    await wrapper.vm.$nextTick()

    const rows = wrapper.findAll('.tags-row')
    expect(rows).toHaveLength(1)
    expect(rows[0].text()).toContain('A.m4b')
  })

  it('narrows to the files a write would change', async () => {
    const wrapper = await mountView(
      table([
        row({ fileId: 1, fileName: 'Wrong.m4b', mismatched: ['album'] }),
        row({ fileId: 2, fileName: 'Right.m4b', mismatched: [] }),
      ]),
    )

    await wrapper.find('.toolbar-toggle input').setValue(true)
    await wrapper.vm.$nextTick()

    const rows = wrapper.findAll('.tags-row')
    expect(rows).toHaveLength(1)
    expect(rows[0].text()).toContain('Wrong.m4b')
  })

  it('sorts by a column, and sorts empty values last in both directions', async () => {
    const wrapper = await mountView(
      table([
        row({ fileId: 1, fileName: 'C.m4b', tags: { title: 'Charlie' } }),
        row({ fileId: 2, fileName: 'A.m4b', tags: {} }),
        row({ fileId: 3, fileName: 'B.m4b', tags: { title: 'Bravo' } }),
      ]),
    )

    const titleHeader = wrapper
      .findAll('.tags-th-label')
      .find((header) => header.text() === 'Title')!
    await titleHeader.trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.findAll('.tags-row').map((r) => r.text())).toEqual([
      expect.stringContaining('Bravo'),
      expect.stringContaining('Charlie'),
      expect.stringContaining('A.m4b'),
    ])

    await titleHeader.trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.findAll('.tags-row').map((r) => r.text())).toEqual([
      expect.stringContaining('Charlie'),
      expect.stringContaining('Bravo'),
      expect.stringContaining('A.m4b'),
    ])
  })

  it('opens the book’s Tags tab when a row is clicked', async () => {
    const wrapper = await mountView(table([row({ audiobookId: 42 })]))

    await wrapper.find('.tags-row').trigger('click')

    expect(push).toHaveBeenCalledWith({
      name: 'audiobook-detail',
      params: { id: 42 },
      query: { tab: 'tags' },
    })
  })

  it('re-reads from disk only when asked', async () => {
    const wrapper = await mountView()
    expect(getLibraryTags).toHaveBeenLastCalledWith(false)

    const reread = wrapper.findAll('.toolbar-btn').at(-1)!
    await reread.trigger('click')

    expect(getLibraryTags).toHaveBeenLastCalledWith(true)
  })

  it('drops a remembered column the catalog no longer has', async () => {
    // A stored list outliving a tag is better handled by losing the column than by
    // rendering one that is permanently blank.
    localStorage.setItem('listenarr.tagsView.columns.v2', JSON.stringify(['album', 'retired_tag']))

    const wrapper = await mountView()

    const headers = wrapper.findAll('.tags-th-label').map((header) => header.text())
    expect(headers).toEqual(['Filename', 'Album'])
  })

  it('reports the failure instead of an empty table', async () => {
    getLibraryTags.mockRejectedValue(new Error('the share went away'))
    const { default: TagsView } = await import('@/views/library/TagsView.vue')

    const wrapper = mount(TagsView)
    await new Promise((resolve) => setTimeout(resolve, 0))
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.tags-state--error').text()).toContain('the share went away')
  })
})

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
import { describe, expect, it } from 'vitest'
import {
  buildCollectionSections,
  buildLibrarySections,
  getGroupingSeries,
} from '@/utils/libraryGrouping'
import type { Audiobook } from '@/types'

function book(partial: Partial<Audiobook> & { id: number; title: string }): Audiobook {
  return { files: [], ...partial } as Audiobook
}

const leviathan = book({
  id: 1,
  title: 'Leviathan Wakes',
  authors: ['James Corey'],
  seriesMemberships: [{ seriesName: 'The Expanse', seriesNumber: '1', isPrimary: true }],
})
const calibans = book({
  id: 2,
  title: "Caliban's War",
  authors: ['James Corey'],
  seriesMemberships: [{ seriesName: 'The Expanse', seriesNumber: '2', isPrimary: true }],
})
const standalone = book({ id: 3, title: 'Drive', authors: ['James Corey'] })
const other = book({
  id: 4,
  title: 'Neuromancer',
  authors: ['William Gibson'],
  series: 'Sprawl',
  seriesNumber: '1',
})

describe('buildLibrarySections', () => {
  it('returns the list untouched in one unheaded section when nothing is grouped', () => {
    const books = [calibans, leviathan, other]
    const sections = buildLibrarySections(books, { byAuthor: false, bySeries: false })

    expect(sections).toHaveLength(1)
    expect(sections[0]!.headers).toEqual([])
    expect(sections[0]!.books).toBe(books)
  })

  it('heads one section per author, filed by last name with unknowns last', () => {
    const unknown = book({ id: 5, title: 'Anonymous' })
    const sections = buildLibrarySections([other, leviathan, unknown], {
      byAuthor: true,
      bySeries: false,
    })

    expect(sections.map((s) => s.headers[0]!.label)).toEqual([
      'James Corey',
      'William Gibson',
      'Unknown Author',
    ])
    expect(sections[0]!.headers[0]!.count).toBe(1)
  })

  it('orders books inside a series by series position, not by the incoming sort', () => {
    const sections = buildLibrarySections([calibans, leviathan], {
      byAuthor: false,
      bySeries: true,
    })

    expect(sections).toHaveLength(1)
    expect(sections[0]!.headers[0]!.label).toBe('The Expanse')
    expect(sections[0]!.books.map((b) => b.id)).toEqual([1, 2])
  })

  it('files series-less books under Standalone, last', () => {
    const sections = buildLibrarySections([standalone, leviathan], {
      byAuthor: false,
      bySeries: true,
    })

    expect(sections.map((s) => s.headers[0]!.label)).toEqual(['The Expanse', 'Standalone'])
  })

  it('names the collection each heading opens, and leaves the catch-alls nameless', () => {
    const unknown = book({ id: 5, title: 'Anonymous' })
    const sections = buildLibrarySections([leviathan, standalone, unknown], {
      byAuthor: true,
      bySeries: true,
    })

    const headers = sections.flatMap((s) => s.headers)
    expect(headers.map((h) => [h.type, h.value])).toEqual([
      ['author', 'James Corey'],
      ['series', 'The Expanse'],
      ['series', undefined],
      ['author', undefined],
      ['series', undefined],
    ])
  })

  it('nests series inside authors, with the author heading on its first section only', () => {
    const sections = buildLibrarySections([standalone, other, calibans, leviathan], {
      byAuthor: true,
      bySeries: true,
    })

    expect(sections.map((s) => s.headers.map((h) => `${h.level}:${h.label}`).join(' + '))).toEqual([
      '1:James Corey + 2:The Expanse',
      '2:Standalone',
      '1:William Gibson + 2:Sprawl',
    ])

    // The author heading counts every book of that author, not just its first section's
    expect(sections[0]!.headers[0]!.count).toBe(3)
    expect(sections[0]!.books.map((b) => b.id)).toEqual([1, 2])
    expect(sections[1]!.books.map((b) => b.id)).toEqual([3])
  })

  it('never lists a book twice when it belongs to several series', () => {
    const crossover = book({
      id: 6,
      title: "Ender's Shadow",
      authors: ['Orson Card'],
      seriesMemberships: [
        { seriesName: 'Enderverse', seriesNumber: '7.5', sortOrder: 1 },
        { seriesName: "Ender's Saga", seriesNumber: '1.1', isPrimary: true, sortOrder: 2 },
      ],
    })

    expect(getGroupingSeries(crossover)).toBe("Ender's Saga")

    const sections = buildLibrarySections([crossover], { byAuthor: false, bySeries: true })
    expect(sections.flatMap((s) => s.books)).toHaveLength(1)
  })
})

describe('buildCollectionSections', () => {
  const collections = [
    { name: 'The Expanse', count: 9, author: 'James Corey' },
    { name: 'Sprawl', count: 3, author: 'William Gibson' },
    { name: 'Orphaned', count: 1 },
    { name: "Ender's Saga", count: 5, author: 'Orson Card' },
  ]

  it('leaves the list alone when author grouping is off', () => {
    const sections = buildCollectionSections(collections, false)

    expect(sections).toHaveLength(1)
    expect(sections[0]!.header).toBeUndefined()
    expect(sections[0]!.collections).toBe(collections)
  })

  it('heads collections by author, by last name, authorless last', () => {
    const sections = buildCollectionSections(collections, true)

    expect(sections.map((s) => s.header!.label)).toEqual([
      'Orson Card',
      'James Corey',
      'William Gibson',
      'Unknown Author',
    ])
    // The heading counts collections, not the books inside them
    expect(sections[0]!.header!.count).toBe(1)
    expect(sections[0]!.header!.value).toBe('Orson Card')
    expect(sections[3]!.header!.value).toBeUndefined()
  })
})

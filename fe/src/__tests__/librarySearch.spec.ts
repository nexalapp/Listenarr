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
import { describe, it, expect } from 'vitest'
import { searchLibrary } from '@/utils/librarySearch'
import type { Audiobook } from '@/types'

interface BookSeed {
  id: number
  title: string
  authors?: string[]
  authorAsins?: string[]
  imageUrl?: string
  asin?: string
  series?: string
  seriesNumber?: string
  seriesMemberships?: Array<{ seriesName: string; seriesNumber?: string; isPrimary?: boolean }>
}

const book = (seed: BookSeed): Audiobook => seed as unknown as Audiobook

const atlas = [
  book({
    id: 1,
    title: 'The Atlas Six',
    authors: ['Olivie Blake'],
    authorAsins: ['B08X'],
    imageUrl: '/covers/six.jpg',
    seriesMemberships: [{ seriesName: 'The Atlas', seriesNumber: '1', isPrimary: true }],
  }),
  book({
    id: 2,
    title: 'The Atlas Paradox',
    authors: ['Olivie Blake'],
    authorAsins: ['B08X'],
    imageUrl: '/covers/paradox.jpg',
    seriesMemberships: [{ seriesName: 'The Atlas', seriesNumber: '2', isPrimary: true }],
  }),
  book({
    id: 3,
    title: 'The Atlas Complex',
    authors: ['Olivie Blake'],
    authorAsins: ['B08X'],
    imageUrl: '/covers/complex.jpg',
    seriesMemberships: [{ seriesName: 'The Atlas', seriesNumber: '3', isPrimary: true }],
  }),
]

describe('library header search', () => {
  it('answers an author query with the author, carrying their portrait and shelf size', () => {
    const [hit] = searchLibrary(atlas, 'olivie')

    expect(hit?.kind).toBe('author')
    expect(hit?.title).toBe('Olivie Blake')
    expect(hit?.sub).toBe('Author · 3 books')
    expect(hit?.collection).toEqual({ type: 'author', name: 'Olivie Blake' })
    expect(hit?.covers[0]).toContain('/images/B08X')
  })

  it('answers a series query with the series, stacking its first two books in order', () => {
    const series = searchLibrary(atlas, 'the atlas').find((hit) => hit.kind === 'series')

    expect(series?.title).toBe('The Atlas')
    expect(series?.sub).toBe('Series · 3 books')
    expect(series?.collection).toEqual({ type: 'series', name: 'The Atlas' })
    // Reading order, not the order they were scanned in: #1 in front, #2 behind it.
    expect(series?.covers).toEqual(['/covers/six.jpg', '/covers/paradox.jpg'])
  })

  it('gives a one-book series a single cover, which the row draws full size', () => {
    const lone = [
      book({
        id: 9,
        title: 'Januaries',
        authors: ['Olivie Blake'],
        imageUrl: '/covers/januaries.jpg',
        seriesMemberships: [{ seriesName: 'Shorts', seriesNumber: '1', isPrimary: true }],
      }),
    ]

    const series = searchLibrary(lone, 'shorts').find((hit) => hit.kind === 'series')

    expect(series?.covers).toEqual(['/covers/januaries.jpg'])
    expect(series?.sub).toBe('Series · 1 book')
  })

  it('puts collections above the books, and the books in reach beneath them', () => {
    const kinds = searchLibrary(atlas, 'atlas').map((hit) => hit.kind)

    expect(kinds.filter((kind) => kind === 'series')).toHaveLength(1)
    expect(kinds.indexOf('series')).toBeLessThan(kinds.indexOf('book'))
    expect(searchLibrary(atlas, 'atlas').filter((hit) => hit.kind === 'book')).toHaveLength(3)
  })

  it('ranks a book found by its own title above one found through its author', () => {
    const library = [
      ...atlas,
      book({ id: 4, title: 'One for My Enemy', authors: ['Olivie Blake'] }),
      book({ id: 5, title: 'Atlas Shrugged', authors: ['Ayn Rand'] }),
    ]

    const titles = searchLibrary(library, 'atlas')
      .filter((hit) => hit.kind === 'book')
      .map((hit) => hit.title)

    expect(titles[0]).toBe('Atlas Shrugged')
    expect(titles).not.toContain('One for My Enemy')
  })

  it('finds a co-writer, not only the name on the spine', () => {
    const library = [
      book({
        id: 6,
        title: 'Leviathan Wakes',
        authors: ['James S. A. Corey', 'Ty Franck'],
        authorAsins: ['B001', 'B002'],
      }),
    ]

    const hit = searchLibrary(library, 'franck').find((h) => h.kind === 'author')

    expect(hit?.title).toBe('Ty Franck')
    // The ASIN at the author's own index, so the row is not the headliner's portrait.
    expect(hit?.covers[0]).toContain('/images/B002')
  })

  it('finds a book by the second half of its author, ranking the person first', () => {
    const hits = searchLibrary(atlas, 'blake')

    expect(hits[0]?.kind).toBe('author')
    expect(hits[0]?.title).toBe('Olivie Blake')
  })

  it('keeps each kind inside its own limit, so one author cannot fill the panel', () => {
    const many = Array.from({ length: 20 }, (_, index) =>
      book({
        id: 100 + index,
        title: `Atlas Volume ${index}`,
        authors: [`Atlas Author ${index}`],
        seriesMemberships: [{ seriesName: `Atlas Series ${index}`, seriesNumber: '1' }],
      }),
    )

    const hits = searchLibrary(many, 'atlas')

    expect(hits.filter((hit) => hit.kind === 'author')).toHaveLength(3)
    expect(hits.filter((hit) => hit.kind === 'series')).toHaveLength(3)
    expect(hits.filter((hit) => hit.kind === 'book')).toHaveLength(6)
  })

  it('counts a cross-series book under each of its series', () => {
    const crossed = [
      book({
        id: 7,
        title: "Ender's Shadow",
        authors: ['Orson Scott Card'],
        seriesMemberships: [
          { seriesName: 'Enderverse', seriesNumber: '7.5', isPrimary: true },
          { seriesName: "Ender's Shadow Saga", seriesNumber: '1' },
        ],
      }),
    ]

    expect(searchLibrary(crossed, 'enderverse')[0]?.kind).toBe('series')
    expect(searchLibrary(crossed, 'shadow saga').find((h) => h.kind === 'series')?.title).toBe(
      "Ender's Shadow Saga",
    )
  })

  it('reads the legacy single-series fields when a book carries no memberships', () => {
    const legacy = [
      book({
        id: 8,
        title: 'Old Record',
        authors: ['Someone'],
        series: 'The Expanse',
        seriesNumber: '7',
        imageUrl: '/covers/old.jpg',
      }),
    ]

    const hit = searchLibrary(legacy, 'expanse').find((h) => h.kind === 'series')

    expect(hit?.title).toBe('The Expanse')
    expect(hit?.covers).toEqual(['/covers/old.jpg'])
  })

  it('falls back to the cached cover for a book with no image of its own', () => {
    const hit = searchLibrary([book({ id: 10, title: 'Bare', asin: 'B0BOOK' })], 'bare')[0]

    expect(hit?.covers[0]).toContain('/images/B0BOOK')
  })

  it('offers no cover rather than a stranger when the author resolved to no ASIN', () => {
    const library = [
      book({ id: 11, title: 'A Case of Need', authors: ['Michael Crichton', 'Pen Name'] }),
    ]

    expect(searchLibrary(library, 'pen name')[0]?.covers).toEqual([])
  })

  it('says nothing at all for an empty query or an empty library', () => {
    expect(searchLibrary(atlas, '   ')).toEqual([])
    expect(searchLibrary([], 'atlas')).toEqual([])
    expect(searchLibrary(null, 'atlas')).toEqual([])
  })
})

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
import { collectionCountLabel, groupCollectionRows } from '@/utils/collectionRows'

interface Book {
  title: string
  authors?: string[]
  series?: string[]
  position?: number
  cover?: string
}

const namesOf = (book: Book) => book.authors || []
const seriesOf = (book: Book) => book.series || []
const coverOf = (book: Book) => book.cover

describe('collection count label', () => {
  it('names the kind and counts the books, singular when there is one', () => {
    expect(collectionCountLabel('author', 1)).toBe('Author · 1 book')
    expect(collectionCountLabel('author', 12)).toBe('Author · 12 books')
    expect(collectionCountLabel('series', 1)).toBe('Series · 1 book')
    expect(collectionCountLabel('series', 3)).toBe('Series · 3 books')
  })
})

describe('grouping books into collection rows', () => {
  it('counts the books behind each name and offers the biggest first', () => {
    const rows = groupCollectionRows(
      [
        { title: 'A', authors: ['Olivie Blake'] },
        { title: 'B', authors: ['Olivie Blake'] },
        { title: 'C', authors: ['Ayn Rand'] },
      ],
      'author',
      { namesOf, coverOf },
    )

    expect(rows.map((row) => [row.name, row.count])).toEqual([
      ['Olivie Blake', 2],
      ['Ayn Rand', 1],
    ])
    expect(rows[0]?.kind).toBe('author')
  })

  it('treats one name spelled two ways as one collection, keeping the first spelling', () => {
    const rows = groupCollectionRows(
      [
        { title: 'A', authors: ['Olivie Blake'] },
        { title: 'B', authors: ['olivie  blake'] },
      ],
      'author',
      { namesOf, coverOf },
    )

    expect(rows).toHaveLength(1)
    expect(rows[0]?.name).toBe('Olivie Blake')
    expect(rows[0]?.count).toBe(2)
  })

  it('counts a book once for a name it happens to carry twice', () => {
    const rows = groupCollectionRows([{ title: 'A', authors: ['Blake', 'Blake'] }], 'author', {
      namesOf,
      coverOf,
    })

    expect(rows[0]?.count).toBe(1)
  })

  it('counts a book under every collection it belongs to', () => {
    const rows = groupCollectionRows(
      [{ title: "Ender's Shadow", series: ['Enderverse', "Ender's Shadow Saga"] }],
      'series',
      { namesOf: seriesOf, coverOf },
    )

    expect(rows.map((row) => row.name).sort()).toEqual(["Ender's Shadow Saga", 'Enderverse'])
  })

  it('stacks at most two covers, in reading order when a position is known', () => {
    const rows = groupCollectionRows(
      [
        { title: 'Third', series: ['Sun Eater'], position: 3, cover: '/three.jpg' },
        { title: 'First', series: ['Sun Eater'], position: 1, cover: '/one.jpg' },
        { title: 'Second', series: ['Sun Eater'], position: 2, cover: '/two.jpg' },
      ],
      'series',
      { namesOf: seriesOf, coverOf, positionOf: (book) => book.position },
    )

    expect(rows[0]?.covers).toEqual(['/one.jpg', '/two.jpg'])
  })

  it('keeps the order they arrived in when nothing says which comes first', () => {
    const rows = groupCollectionRows(
      [
        { title: 'B', series: ['Shorts'], cover: '/b.jpg' },
        { title: 'A', series: ['Shorts'], cover: '/a.jpg' },
      ],
      'series',
      { namesOf: seriesOf, coverOf, positionOf: () => undefined },
    )

    expect(rows[0]?.covers).toEqual(['/b.jpg', '/a.jpg'])
  })

  it('puts a book with no position behind the ones that have one', () => {
    const rows = groupCollectionRows(
      [
        { title: 'Unplaced', series: ['Sun Eater'], cover: '/unplaced.jpg' },
        { title: 'First', series: ['Sun Eater'], position: 1, cover: '/one.jpg' },
      ],
      'series',
      { namesOf: seriesOf, coverOf, positionOf: (book) => book.position },
    )

    expect(rows[0]?.covers).toEqual(['/one.jpg', '/unplaced.jpg'])
  })

  it('never stacks the same cover twice, which would read as a rendering fault', () => {
    const rows = groupCollectionRows(
      [
        { title: 'A', series: ['Shorts'], cover: '/same.jpg' },
        { title: 'B', series: ['Shorts'], cover: '/same.jpg' },
      ],
      'series',
      { namesOf: seriesOf, coverOf },
    )

    expect(rows[0]?.covers).toEqual(['/same.jpg'])
    expect(rows[0]?.count).toBe(2)
  })

  it('skips a book with no cover rather than leaving a hole in the stack', () => {
    const rows = groupCollectionRows(
      [
        { title: 'A', series: ['Shorts'] },
        { title: 'B', series: ['Shorts'], cover: '/b.jpg' },
      ],
      'series',
      { namesOf: seriesOf, coverOf },
    )

    expect(rows[0]?.covers).toEqual(['/b.jpg'])
  })

  it('ignores blank and whitespace-only names', () => {
    const rows = groupCollectionRows(
      [{ title: 'A', authors: ['', '   ', 'Real Name'] }],
      'author',
      { namesOf, coverOf },
    )

    expect(rows.map((row) => row.name)).toEqual(['Real Name'])
  })

  it('breaks a tie on the name, so the same books answer the same way twice running', () => {
    const books = [
      { title: 'A', authors: ['Zadie Smith'] },
      { title: 'B', authors: ['Ann Leckie'] },
    ]

    expect(groupCollectionRows(books, 'author', { namesOf, coverOf }).map((r) => r.name)).toEqual([
      'Ann Leckie',
      'Zadie Smith',
    ])
  })

  it('honours the limit, keeping the largest collections', () => {
    const books = [
      { title: 'A', authors: ['Big'] },
      { title: 'B', authors: ['Big'] },
      { title: 'C', authors: ['Small'] },
      { title: 'D', authors: ['Other'] },
    ]

    const rows = groupCollectionRows(books, 'author', { namesOf, coverOf, limit: 1 })

    expect(rows.map((row) => row.name)).toEqual(['Big'])
  })

  it('says nothing for an empty or missing set of books', () => {
    expect(groupCollectionRows([], 'author', { namesOf, coverOf })).toEqual([])
    expect(groupCollectionRows(null, 'author', { namesOf, coverOf })).toEqual([])
    expect(groupCollectionRows(undefined, 'series', { namesOf: seriesOf, coverOf })).toEqual([])
  })
})

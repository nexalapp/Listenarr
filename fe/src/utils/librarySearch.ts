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
import type { Audiobook } from '@/types'
import { getAuthorAsin, getAuthorCoverUrl, getBookCoverUrl } from '@/utils/libraryCovers'
import { getSeriesNames } from '@/utils/seriesUtils'

/**
 * What the header search offers for a query. An author and a series are things in the
 * library in their own right — each has a page — so typing a name finds the person or
 * the series itself, not only the books that mention them.
 */
export type LibrarySearchKind = 'author' | 'series' | 'book'

export interface LibrarySearchHit {
  kind: LibrarySearchKind
  /** Unique across kinds, so one list can hold all three. */
  key: string
  /** The line that names the thing: a book's title, or the author's or series' name. */
  title: string
  /** Under it: a book's author, or what this row is — "Series · 3 books". */
  sub: string
  /**
   * What to draw in the thumb. A book or an author has at most one; a series carries up
   * to two, newest-first in reading order, to be stacked.
   */
  covers: string[]
  /** The book to open. Books only. */
  bookId?: number
  /** The page to open. Authors and series only. */
  collection?: { type: 'author' | 'series'; name: string }
  /** How many of the library's books the row stands for. Authors and series only. */
  count?: number
}

export interface LibrarySearchLimits {
  authors?: number
  series?: number
  books?: number
}

/**
 * Three of each collection is enough to recognise the one you meant without pushing the
 * books — still the common target — out of sight.
 */
export const DEFAULT_LIBRARY_SEARCH_LIMITS: Required<LibrarySearchLimits> = {
  authors: 3,
  series: 3,
  books: 6,
}

/** Case and stray spacing are noise to a search box; everything else is a real letter. */
function normalize(value: string | null | undefined): string {
  return (value || '').toLowerCase().replace(/\s+/g, ' ').trim()
}

/**
 * How well a name answers the query, lowest first: the whole name, then the start of it,
 * then the start of any word in it ("blake" for "Olivie Blake"), then anywhere at all.
 * Typing a surname should not put a book whose publisher happens to contain it on top.
 */
function matchRank(name: string, query: string): number {
  const subject = normalize(name)
  if (!subject.includes(query)) return -1
  if (subject === query) return 0
  if (subject.startsWith(query)) return 1
  if (subject.includes(` ${query}`)) return 2
  return 3
}

/** No name in the set answers the query at all. */
const NO_MATCH = Number.MAX_SAFE_INTEGER

/** The best rank any of these names earns, or {@link NO_MATCH} when none does. */
function bestRank(names: Array<string | null | undefined>, query: string): number {
  let best = NO_MATCH
  for (const name of names) {
    const rank = matchRank(name || '', query)
    if (rank >= 0 && rank < best) best = rank
  }
  return best
}

/** The book's position in one of its series, for ordering that series' covers. */
function seriesPosition(book: Audiobook, seriesName: string): number {
  const target = normalize(seriesName)
  const membership = (book.seriesMemberships || []).find(
    (candidate) => normalize(candidate.seriesName) === target,
  )
  const raw = membership
    ? membership.seriesNumber
    : normalize(book.series) === target
      ? book.seriesNumber
      : undefined
  const position = parseFloat((raw || '').trim())
  return Number.isFinite(position) ? position : Number.MAX_SAFE_INTEGER
}

interface CollectionAccumulator {
  name: string
  rank: number
  count: number
  cover?: string
  books: Audiobook[]
}

function countLabel(kind: 'Author' | 'Series', count: number): string {
  return `${kind} · ${count === 1 ? '1 book' : `${count} books`}`
}

function take(groups: Map<string, CollectionAccumulator>, limit: number): CollectionAccumulator[] {
  return Array.from(groups.values())
    .sort(
      (a, b) =>
        a.rank - b.rank ||
        b.count - a.count ||
        a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
    )
    .slice(0, Math.max(0, limit))
}

/**
 * The library's answer to a header query: the authors it names, then the series, then
 * the books. Collections lead because a name usually means the person or the series —
 * their books follow directly beneath, so nothing is hidden by putting them first.
 */
export function searchLibrary(
  books: Audiobook[] | null | undefined,
  query: string,
  limits: LibrarySearchLimits = {},
): LibrarySearchHit[] {
  const subject = normalize(query)
  if (!subject) return []

  const {
    authors: authorLimit,
    series: seriesLimit,
    books: bookLimit,
  } = {
    ...DEFAULT_LIBRARY_SEARCH_LIMITS,
    ...limits,
  }

  const library = books || []
  const authorGroups = new Map<string, CollectionAccumulator>()
  const seriesGroups = new Map<string, CollectionAccumulator>()
  const bookHits: Array<{ rank: number; book: Audiobook }> = []

  for (const book of library) {
    // Every author, not only the first: a co-writer is as searchable as a headliner.
    for (const author of book.authors || []) {
      const name = (author || '').trim()
      if (!name) continue
      const rank = matchRank(name, subject)
      if (rank < 0) continue
      const group = authorGroups.get(normalize(name)) ?? { name, rank, count: 0, books: [] }
      group.count += 1
      group.cover = group.cover ?? getAuthorCoverUrl(getAuthorAsin(book, name))
      authorGroups.set(normalize(name), group)
    }

    for (const seriesName of getSeriesNames(book)) {
      const rank = matchRank(seriesName, subject)
      if (rank < 0) continue
      const group = seriesGroups.get(normalize(seriesName)) ?? {
        name: seriesName,
        rank,
        count: 0,
        books: [],
      }
      group.count += 1
      group.books.push(book)
      seriesGroups.set(normalize(seriesName), group)
    }

    // A book found through its author or its series ranks below one found by its own
    // title, so "atlas" lists The Atlas Six above the rest of a matching author's shelf.
    const borrowed = bestRank([...(book.authors || []), ...getSeriesNames(book)], subject)
    const rank = Math.min(
      bestRank([book.title], subject),
      borrowed === NO_MATCH ? NO_MATCH : borrowed + 4,
    )
    if (rank < NO_MATCH) bookHits.push({ rank, book })
  }

  const hits: LibrarySearchHit[] = []

  for (const group of take(authorGroups, authorLimit)) {
    hits.push({
      kind: 'author',
      key: `author:${group.name}`,
      title: group.name,
      sub: countLabel('Author', group.count),
      covers: group.cover ? [group.cover] : [],
      collection: { type: 'author', name: group.name },
      count: group.count,
    })
  }

  for (const group of take(seriesGroups, seriesLimit)) {
    // The first two of the series as it is read, which is what makes the stack say
    // which series this is rather than which book happened to be scanned first.
    const covers: string[] = []
    for (const book of group.books
      .slice()
      .sort((a, b) => seriesPosition(a, group.name) - seriesPosition(b, group.name))) {
      const cover = getBookCoverUrl(book)
      if (cover && !covers.includes(cover)) covers.push(cover)
      if (covers.length === 2) break
    }

    hits.push({
      kind: 'series',
      key: `series:${group.name}`,
      title: group.name,
      sub: countLabel('Series', group.count),
      covers,
      collection: { type: 'series', name: group.name },
      count: group.count,
    })
  }

  for (const { book } of bookHits
    .sort((a, b) => a.rank - b.rank)
    .slice(0, Math.max(0, bookLimit))) {
    const cover = getBookCoverUrl(book)
    hits.push({
      kind: 'book',
      key: `book:${book.id}`,
      title: book.title || 'Unknown',
      sub: (book.authors || [])[0] || '',
      covers: cover ? [cover] : [],
      bookId: book.id,
    })
  }

  return hits
}

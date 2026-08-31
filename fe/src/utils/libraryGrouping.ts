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
import type { Audiobook, AudiobookSeriesMembership } from '@/types'

export const UNKNOWN_AUTHOR_LABEL = 'Unknown Author'
export const NO_SERIES_LABEL = 'Standalone'

export interface LibraryGroupingOptions {
  byAuthor: boolean
  bySeries: boolean
}

/** One heading printed above a section's books. Level 1 is the outer grouping. */
export interface LibraryGroupHeader {
  key: string
  label: string
  level: 1 | 2
  /** Books under this heading, including the ones in its later sibling sections. */
  count: number
  type: 'author' | 'series'
  /**
   * The author or series this heading stands for, and so the collection it opens.
   * Absent on the catch-all buckets, which name no collection anything can link to.
   */
  value?: string
}

/**
 * A contiguous run of books preceded by zero or more headings. Grouping by author and
 * by series at once produces one section per (author, series) pair; the author heading
 * rides on the first section of each author, so a heading is never repeated.
 */
export interface LibrarySection<T = Audiobook> {
  key: string
  headers: LibraryGroupHeader[]
  books: T[]
}

type SeriesBearer = Pick<Audiobook, 'series' | 'seriesNumber' | 'seriesMemberships'>

/** A collection card/row — an author or a series — as the grouped views build it. */
export interface AuthoredCollection {
  name: string
  count: number
  /** The author the collection is filed under, when one applies. */
  author?: string
}

/** A run of collections under at most one heading. */
export interface CollectionSection<T extends AuthoredCollection> {
  key: string
  header?: LibraryGroupHeader
  collections: T[]
}

/**
 * The author a book is filed under. Only the first author groups the book: a book with
 * several authors appearing under each of them would show up more than once in one list,
 * which breaks range selection and the count badge.
 */
export function getGroupingAuthor(book: Pick<Audiobook, 'authors'>): string {
  const author = (book.authors?.[0] || '').trim()
  return author || UNKNOWN_AUTHOR_LABEL
}

/** The membership a book is filed under: the primary one, else the lowest sort order. */
export function getPrimarySeriesMembership(
  book: SeriesBearer,
): AudiobookSeriesMembership | undefined {
  const memberships = (book.seriesMemberships || []).filter((m) => (m.seriesName || '').trim())
  if (memberships.length === 0) return undefined

  const primary = memberships.find((m) => m.isPrimary)
  if (primary) return primary

  return memberships.reduce((best, candidate) => {
    const bestOrder = best.sortOrder ?? Number.MAX_SAFE_INTEGER
    const candidateOrder = candidate.sortOrder ?? Number.MAX_SAFE_INTEGER
    return candidateOrder < bestOrder ? candidate : best
  })
}

/** The series a book is filed under, falling back to the legacy single-series fields. */
export function getGroupingSeries(book: SeriesBearer): string {
  const membership = getPrimarySeriesMembership(book)
  if (membership) return (membership.seriesName || '').trim() || NO_SERIES_LABEL
  return (book.series || '').trim() || NO_SERIES_LABEL
}

function getGroupingSeriesNumber(book: SeriesBearer): string {
  const membership = getPrimarySeriesMembership(book)
  const raw = membership ? membership.seriesNumber : book.seriesNumber
  return (raw || '').trim()
}

/**
 * Books in a series read in series order, not in whatever order the toolbar sort left
 * them: a series heading exists to show the reading order. Books with no number keep
 * the incoming sort and sit after the numbered ones.
 */
function compareBySeriesPosition(a: SeriesBearer, b: SeriesBearer): number {
  const an = parseFloat(getGroupingSeriesNumber(a))
  const bn = parseFloat(getGroupingSeriesNumber(b))
  const aHas = Number.isFinite(an)
  const bHas = Number.isFinite(bn)
  if (aHas && bHas) return an - bn
  if (aHas) return -1
  if (bHas) return 1
  return 0
}

/** "Last First", so author headings file the way a shelf does. */
function authorHeadingKey(name: string): string {
  const parts = name.trim().split(/\s+/)
  if (parts.length <= 1) return (parts[0] || '').toLowerCase()
  return `${parts[parts.length - 1]} ${parts[0]}`.toLowerCase()
}

/**
 * Headings always read A→Z regardless of the toolbar sort direction — a grouping is an
 * index — with the catch-all bucket ("Unknown Author", "Standalone") pinned last.
 */
function compareHeadings(
  a: string,
  b: string,
  catchAll: string,
  keyOf: (name: string) => string,
): number {
  if (a === catchAll) return b === catchAll ? 0 : 1
  if (b === catchAll) return -1
  return keyOf(a).localeCompare(keyOf(b))
}

function groupInOrder<T>(items: T[], keyOf: (item: T) => string): Map<string, T[]> {
  const groups = new Map<string, T[]>()
  for (const item of items) {
    const key = keyOf(item)
    const bucket = groups.get(key)
    if (bucket) bucket.push(item)
    else groups.set(key, [item])
  }
  return groups
}

/**
 * Lays an already-filtered, already-sorted list of books out as headed sections.
 *
 * With no grouping enabled this is one unheaded section holding the list untouched, so
 * the caller renders grouped and ungrouped libraries through one path.
 */
export function buildLibrarySections<T extends Audiobook>(
  books: T[],
  options: LibraryGroupingOptions,
): LibrarySection<T>[] {
  const { byAuthor, bySeries } = options

  if (!byAuthor && !bySeries) {
    return [{ key: 'all', headers: [], books }]
  }

  if (!byAuthor) {
    return seriesSections(books, '')
  }

  const authors = Array.from(groupInOrder(books, getGroupingAuthor).entries()).sort((a, b) =>
    compareHeadings(a[0], b[0], UNKNOWN_AUTHOR_LABEL, authorHeadingKey),
  )

  const sections: LibrarySection<T>[] = []
  for (const [author, authorBooks] of authors) {
    const authorHeader: LibraryGroupHeader = {
      key: `author:${author}`,
      label: author,
      level: 1,
      count: authorBooks.length,
      type: 'author',
      ...(author === UNKNOWN_AUTHOR_LABEL ? {} : { value: author }),
    }

    if (!bySeries) {
      sections.push({ key: `author:${author}`, headers: [authorHeader], books: authorBooks })
      continue
    }

    // The author heading rides on the author's first series section rather than sitting
    // in a section of its own, so no heading row is ever left with no books under it.
    const inner = seriesSections(authorBooks, `author:${author}|`)
    inner.forEach((section, index) => {
      for (const header of section.headers) header.level = 2
      sections.push(
        index === 0 ? { ...section, headers: [authorHeader, ...section.headers] } : section,
      )
    })
  }

  return sections
}

function seriesSections<T extends Audiobook>(books: T[], keyPrefix: string): LibrarySection<T>[] {
  const series = Array.from(groupInOrder(books, getGroupingSeries).entries()).sort((a, b) =>
    compareHeadings(a[0], b[0], NO_SERIES_LABEL, (name) => name.toLowerCase()),
  )

  return series.map(([name, seriesBooks]) => ({
    key: `${keyPrefix}series:${name}`,
    headers: [
      {
        key: `${keyPrefix}series:${name}`,
        label: name,
        level: 1 as const,
        count: seriesBooks.length,
        type: 'series' as const,
        ...(name === NO_SERIES_LABEL ? {} : { value: name }),
      },
    ],
    books:
      name === NO_SERIES_LABEL ? seriesBooks : seriesBooks.slice().sort(compareBySeriesPosition),
  }))
}

/**
 * The collection equivalent of `buildLibrarySections`: heads a list of series by the
 * author whose books they hold. Collections carry their own order, so the only thing
 * grouping changes is which heading each falls under.
 */
export function buildCollectionSections<T extends AuthoredCollection>(
  collections: T[],
  byAuthor: boolean,
): CollectionSection<T>[] {
  if (!byAuthor) return [{ key: 'all', collections }]

  const groups = Array.from(
    groupInOrder(collections, (c) => (c.author || '').trim() || UNKNOWN_AUTHOR_LABEL).entries(),
  ).sort((a, b) => compareHeadings(a[0], b[0], UNKNOWN_AUTHOR_LABEL, authorHeadingKey))

  return groups.map(([author, grouped]) => ({
    key: `author:${author}`,
    header: {
      key: `author:${author}`,
      label: author,
      level: 1,
      count: grouped.length,
      type: 'author',
      ...(author === UNKNOWN_AUTHOR_LABEL ? {} : { value: author }),
    },
    collections: grouped,
  }))
}

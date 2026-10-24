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

/**
 * Turning a set of books into the authors and series they belong to, so each can be
 * offered as a row of its own: the header search does it over the library, and the
 * Add New page does it over whatever a search returned. The counting and the wording
 * live here so the two cannot drift apart.
 */

export type CollectionKind = 'author' | 'series'

export interface CollectionRow {
  kind: CollectionKind
  /** The author's or series' name, as the books spell it. */
  name: string
  /** How many of the books given stand behind this row. */
  count: number
  /** Up to two covers, in reading order where one is known, for stacking in a thumb. */
  covers: string[]
}

/** How many covers a stack can hold before the rest are better left unsaid. */
const MAX_STACKED_COVERS = 2

/** Case and stray spacing are noise when deciding whether two names are the same. */
function normalize(value: string | null | undefined): string {
  return (value || '').toLowerCase().replace(/\s+/g, ' ').trim()
}

/**
 * The line under a collection's name: what kind of thing it is and how much of it
 * there is. Written once, because it appears in the header search and on the Add New
 * page and a reader should not have to wonder whether the two mean the same thing.
 */
export function collectionCountLabel(kind: CollectionKind, count: number): string {
  const noun = kind === 'author' ? 'Author' : 'Series'
  return `${noun} · ${count === 1 ? '1 book' : `${count} books`}`
}

export interface GroupCollectionRowsOptions<T> {
  /** Which collections of this kind each book belongs to; several is normal. */
  namesOf: (item: T) => string[]
  /** The book's cover, or undefined when it has none to contribute. */
  coverOf: (item: T) => string | undefined
  /**
   * The book's position in the named collection, for ordering the covers. Books with
   * no position fall in behind those that have one, keeping the order they arrived in.
   */
  positionOf?: (item: T, name: string) => number | undefined
  /** How many rows to return at most. Everything, when not given. */
  limit?: number
}

/**
 * The collections of one kind that these books belong to, largest first.
 *
 * Ties break on the name rather than on the order the books happened to arrive in, so
 * the same search twice running offers the same rows in the same order.
 */
export function groupCollectionRows<T>(
  items: readonly T[] | null | undefined,
  kind: CollectionKind,
  options: GroupCollectionRowsOptions<T>,
): CollectionRow[] {
  const { namesOf, coverOf, positionOf, limit } = options
  const groups = new Map<string, { name: string; count: number; members: T[] }>()

  for (const item of items || []) {
    // A name appearing twice on one book counts once: it is still one book by them.
    const seen = new Set<string>()
    for (const raw of namesOf(item) || []) {
      const name = (raw || '').trim()
      const key = normalize(name)
      if (!key || seen.has(key)) continue
      seen.add(key)
      const group = groups.get(key) ?? { name, count: 0, members: [] }
      group.count += 1
      group.members.push(item)
      groups.set(key, group)
    }
  }

  const ordered = Array.from(groups.values()).sort(
    (a, b) => b.count - a.count || a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
  )

  return ordered
    .slice(0, limit === undefined ? ordered.length : Math.max(0, limit))
    .map((group) => {
      const members = positionOf
        ? group.members
            .map((item, index) => ({ item, index }))
            .sort((a, b) => {
              const ap = positionOf(a.item, group.name)
              const bp = positionOf(b.item, group.name)
              // A known position wins; two unknowns keep the order they came in.
              if (ap === undefined && bp === undefined) return a.index - b.index
              if (ap === undefined) return 1
              if (bp === undefined) return -1
              return ap - bp || a.index - b.index
            })
            .map((entry) => entry.item)
        : group.members

      const covers: string[] = []
      for (const member of members) {
        const cover = coverOf(member)
        // The same cover twice is not a stack; it reads as a rendering fault.
        if (cover && !covers.includes(cover)) covers.push(cover)
        if (covers.length === MAX_STACKED_COVERS) break
      }

      return { kind, name: group.name, count: group.count, covers }
    })
}

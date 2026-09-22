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
 * Which catalogue books are another publication of a book the library already has.
 *
 * A series' catalogue holds more than its stories: a re-release with new cover art,
 * a second narration, a tie-in edition retitled for a television series. Each is a
 * separate ASIN, so each arrived as one more "Not Added" card, and a series whose
 * every story is on disk still read as incomplete. These are not missing books —
 * the shelf already answers for them — so they are set aside rather than counted.
 *
 * What counts as the same story is deliberately loose, because the publications
 * differ in exactly the ways a strict match would trip over. Two books are the same
 * story when they sit at the same place in the same series, or when their titles
 * match once the packaging is stripped off. Nothing here compares narrators,
 * runtimes or years: a different narration of a book someone owns is still that
 * book, and whether they want it as well is their call, which is why the card is
 * kept rather than hidden.
 */

/** Strip accents and punctuation, as the collection's own matching does. */
function normalize(value: string | undefined | null): string {
  if (!value) return ''
  return value
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim()
}

/**
 * The words a publisher adds around a title, which two publications of one story
 * rarely agree on: the format, the edition, the series it was sold under, and the
 * genre label tacked on after a colon.
 */
const PACKAGING =
  /\b(unabridged|abridged|audiobook|audio\s*book|dramatized|dramatised|adaptation|a\s+novel|a\s+novella|special\s+edition|collector'?s\s+edition|anniversary\s+edition|deluxe\s+edition|movie\s+tie\s*in|tv\s+tie\s*in|media\s+tie\s*in|tie\s*in\s+edition|new\s+edition|revised\s+edition|reissue|remastered|book\s+\d+|volume\s+\d+|vol\s+\d+|part\s+\d+)\b/g

/**
 * A title with the packaging removed, for comparing one publication with another.
 *
 * The subtitle goes first, before normalising, because the punctuation that marks
 * it — a colon, a dash — is exactly what normalising throws away. One publication
 * of Wool is sold as "Wool", another as "Wool: The Silo Saga"; the story is the
 * part in front.
 */
export function storyTitleKey(title: string | undefined | null): string {
  const withoutSubtitle = (title ?? '').split(/\s*[:\u2013\u2014]\s*| - /)[0] ?? ''
  const base = normalize(withoutSubtitle)
  const stripped = base.replace(PACKAGING, ' ').replace(/\s+/g, ' ').trim()

  // "A Novel" alone is packaging, not a title: keep the unstripped form rather than nothing.
  return stripped || base
}

/** The position in a series, as a comparable number; null for anything that is not one. */
export function seriesPositionKey(seriesNumber: string | undefined | null): string | null {
  if (!seriesNumber) return null

  const trimmed = String(seriesNumber).trim()
  // A range — "1-3" — is an omnibus, which stands in for no single story.
  if (!/^\d+(\.\d+)?$/.test(trimmed)) return null

  const value = Number.parseFloat(trimmed)
  return Number.isFinite(value) ? String(value) : null
}

/** What this book is, for deciding whether another publication covers it. */
export interface PublicationIdentity {
  title?: string | null
  seriesNumber?: string | null
  authors?: readonly string[] | null
}

/** The keys under which a book would be recognised as a story someone already has. */
export function storyKeys(book: PublicationIdentity, seriesName: string | null): string[] {
  const keys: string[] = []

  const position = seriesPositionKey(book.seriesNumber)
  const series = normalize(seriesName)
  if (position && series) {
    keys.push(`position:${series}:${position}`)
  }

  const title = storyTitleKey(book.title)
  if (title) {
    keys.push(`title:${title}`)
  }

  return keys
}

/**
 * The keys of the books that are another publication of something already held.
 *
 * @param items Every book on the page, in the order it is shown.
 * @param seriesName The series this page is, or null for a page that is not one series.
 */
export function findAlternatePublications<
  T extends PublicationIdentity & { key: string; inLibrary: boolean },
>(items: readonly T[], seriesName: string | null): Set<string> {
  const held = new Set<string>()
  for (const item of items) {
    if (!item.inLibrary) continue
    for (const key of storyKeys(item, seriesName)) held.add(key)
  }

  const alternates = new Set<string>()
  if (held.size === 0) return alternates

  for (const item of items) {
    if (item.inLibrary) continue

    const keys = storyKeys(item, seriesName)
    if (keys.some((key) => held.has(key))) {
      alternates.add(item.key)
    }
  }

  return alternates
}

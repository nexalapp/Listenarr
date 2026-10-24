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
import { buildApiPath } from '@/services/apiBase'
import type { Audiobook } from '@/types'

/**
 * Which image stands for a book or an author, in one place: the library grid, the
 * header search and anything else that draws a cover should reach for the same one.
 */

/** Whether a URL is the stand-in drawn when a book has no cover of its own. */
export function isPlaceholderCoverUrl(url: string | undefined | null): boolean {
  const value = (url || '').trim()
  if (!value) return true
  return (
    value === '/placeholder.svg' ||
    value === 'placeholder.svg' ||
    value.endsWith('/placeholder.svg') ||
    value.includes('/placeholder.svg?')
  )
}

/**
 * The book's own cover, else the one the API caches against its ASIN. Undefined when
 * neither exists, so the caller can draw the placeholder rather than a broken image.
 */
export function getBookCoverUrl(
  book: Pick<Audiobook, 'imageUrl' | 'asin'> | null | undefined,
): string | undefined {
  if (!book) return undefined
  const raw = (book.imageUrl || '').trim()
  if (raw && !isPlaceholderCoverUrl(raw)) return raw
  const asin = (book.asin || '').trim()
  if (asin) return buildApiPath(`/images/${encodeURIComponent(asin)}`)
  return raw || undefined
}

/** The portrait cached against an author's ASIN, or undefined when none was resolved. */
export function getAuthorCoverUrl(asin: string | null | undefined): string | undefined {
  const trimmed = (asin || '').trim()
  return trimmed ? buildApiPath(`/images/${encodeURIComponent(trimmed)}`) : undefined
}

/**
 * The ASIN Audible gave for one of a book's authors. The two lists are written in step,
 * so the name at an index answers to the ASIN at the same one — but a book can carry
 * fewer ASINs than names (a pen name resolves to nobody), which is why this is a lookup
 * and not an index.
 */
export function getAuthorAsin(
  book: Pick<Audiobook, 'authors' | 'authorAsins'>,
  authorName: string,
): string | undefined {
  const index = (book.authors || []).findIndex(
    (name) => (name || '').trim().toLowerCase() === authorName.trim().toLowerCase(),
  )
  if (index < 0) return undefined
  return (book.authorAsins || [])[index]?.trim() || undefined
}

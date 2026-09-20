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
import { apiService } from '@/services/api'
import type { AudibleBookMetadata, AuthorCatalogBook } from '@/types'

/**
 * A catalog entry as the add flow wants it. Shared by the author, series and
 * suggested pages so a book adds the same way from any of them.
 */
export function buildCatalogMetadata(book: AuthorCatalogBook): AudibleBookMetadata {
  const authors = (book.authors || []).filter(Boolean)
  const publishYear = book.publishedDate?.match(/\d{4}/)?.[0]

  return {
    asin: book.asin || '',
    title: book.title || 'Unknown Title',
    subtitle: book.subtitle,
    authors,
    imageUrl: book.imageUrl,
    runtime: book.runtime,
    language: book.language,
    publisher: book.publisher,
    narrators: book.narrators || [],
    genres: book.genres || [],
    series: book.series,
    seriesNumber: book.seriesNumber,
    publishedDate: book.publishedDate,
    publishYear,
    isbn: book.isbn,
    source: book.metadataSource || 'Audible',
    sourceLink: book.link,
    metadataSource: book.metadataSource || 'Audible',
  }
}

type AudiblePayload = {
  asin?: string
  title?: string
  subtitle?: string
  authors?: { name?: string }[]
  narrators?: { name?: string }[]
  publisher?: string
  publishDate?: string
  releaseDate?: string
  description?: string
  imageUrl?: string
  lengthMinutes?: number
  language?: string
  region?: string
  genres?: { name?: string }[]
  series?: { name?: string; position?: string | number; asin?: string }[]
  bookFormat?: string
  isbn?: string
}

/**
 * The catalog entry filled in from the book's own Audible record, the way the Add
 * modal fills it before it adds: real author and narrator names, every series the
 * book belongs to, the blurb and the genres. A catalog row alone carries only what
 * its listing showed. Returns the row as-is when the lookup fails, so an add is
 * never blocked on enrichment.
 */
export async function enrichCatalogMetadata(book: AuthorCatalogBook): Promise<AudibleBookMetadata> {
  const base = buildCatalogMetadata(book)
  if (!book.asin) return base
  try {
    const resp = await apiService.getAudibleMetadata<
      { source?: string; metadata?: AudiblePayload } | AudiblePayload
    >(book.asin)
    const raw: AudiblePayload =
      resp && 'metadata' in resp && resp.metadata ? resp.metadata : (resp as AudiblePayload)
    const source =
      resp && 'source' in resp && typeof resp.source === 'string' ? resp.source : undefined
    const names = (people?: { name?: string }[]) =>
      (people ?? []).map((p) => p?.name?.trim() ?? '').filter(Boolean)
    const authors = names(raw.authors)
    const narrators = names(raw.narrators)
    const genres = names(raw.genres)
    const memberships = (raw.series ?? [])
      .filter((s) => s?.name?.trim())
      .map((s, index) => ({
        seriesName: s.name!.trim(),
        seriesNumber:
          s.position !== undefined && s.position !== null && String(s.position) !== 'null'
            ? String(s.position)
            : undefined,
        seriesAsin: s.asin,
        isPrimary: index === 0,
        sortOrder: index,
      }))
    const date = raw.publishDate || raw.releaseDate
    return {
      ...base,
      title: raw.title || base.title,
      subtitle: raw.subtitle ?? base.subtitle,
      authors: authors.length ? authors : base.authors,
      narrators: narrators.length ? narrators : base.narrators,
      genres: genres.length ? genres : base.genres,
      publisher: raw.publisher || base.publisher,
      publishedDate: date || base.publishedDate,
      publishYear: date?.match(/\d{4}/)?.[0] || base.publishYear,
      description: raw.description || base.description,
      imageUrl: raw.imageUrl || base.imageUrl,
      runtime: typeof raw.lengthMinutes === 'number' ? raw.lengthMinutes : base.runtime,
      language: raw.language || base.language,
      region: raw.region || base.region,
      isbn: raw.isbn || base.isbn,
      abridged:
        typeof raw.bookFormat === 'string'
          ? raw.bookFormat.toLowerCase().includes('abridged')
          : base.abridged,
      ...(memberships.length > 0
        ? {
            series: memberships[0].seriesName,
            seriesNumber: memberships[0].seriesNumber,
            seriesMemberships: memberships,
          }
        : {}),
      ...(source ? { source, metadataSource: source } : {}),
    }
  } catch {
    return base
  }
}

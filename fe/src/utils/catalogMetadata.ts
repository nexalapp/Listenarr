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

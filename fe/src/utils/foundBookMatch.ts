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
import type { FoundBook, SearchResult } from '@/types'

/**
 * How sure we are that a catalogue result is the book the files say they are.
 *
 * Nothing on the server scores this — the search returns candidates, not
 * verdicts — so it is judged here from what the row knows: the title, the author,
 * and the running time of the files, which a wrong edition rarely matches.
 */
export function matchConfidence(result: SearchResult, book: FoundBook): number {
  let score = 0
  const bookTitle = normalize(book.title)
  const resultTitle = normalize(result.title)
  if (bookTitle && resultTitle) {
    if (bookTitle === resultTitle) score += 0.6
    else if (bookTitle.includes(resultTitle) || resultTitle.includes(bookTitle)) score += 0.45
    else score += 0.4 * tokenOverlap(bookTitle, resultTitle)
  } else if (book.asin && result.asin && book.asin.toUpperCase() === result.asin.toUpperCase()) {
    score += 0.6
  }

  if (book.asin && result.asin && book.asin.toUpperCase() === result.asin.toUpperCase()) {
    score = Math.max(score, 0.95)
  }

  const bookAuthor = normalize(book.author)
  const resultAuthors = (result.authors ?? []).map((a) => normalize(a.name)).filter(Boolean)
  if (bookAuthor && resultAuthors.length > 0) {
    if (resultAuthors.some((a) => authorsAgree(a, bookAuthor))) score += 0.25
  } else if (!bookAuthor) {
    // Nothing to disagree with; give half credit rather than punish untagged files.
    score += 0.1
  }

  const delta = runtimeDeltaSeconds(result, book)
  if (delta != null && book.totalDurationSeconds > 0) {
    const ratio = delta / book.totalDurationSeconds
    if (ratio <= 0.02) score += 0.15
    else if (ratio <= 0.1) score += 0.08
  }

  return Math.min(1, Math.round(score * 100) / 100)
}

/** Seconds between the catalogue's running time and the files', or null when the catalogue gives none. */
export function runtimeDeltaSeconds(result: SearchResult, book: FoundBook): number | null {
  const seconds = result.runtime ?? (result.lengthMinutes ? result.lengthMinutes * 60 : undefined)
  if (!seconds || !book.totalDurationSeconds) return null
  return Math.abs(seconds - book.totalDurationSeconds)
}

export function describeRuntimeDelta(result: SearchResult, book: FoundBook): string | null {
  const delta = runtimeDeltaSeconds(result, book)
  if (delta == null) return null
  if (delta < 120) return `Runtime within ${Math.max(1, Math.round(delta / 60))}m of your files`
  return `Runtime differs by ${clock(delta)}`
}

export function clock(seconds: number): string {
  const total = Math.round(seconds)
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  return h > 0 ? `${h}h ${m}m` : `${m}m`
}

export function confidenceLabel(confidence: number | null): 'matched' | 'low' | 'none' {
  if (confidence == null) return 'none'
  return confidence >= 0.75 ? 'matched' : 'low'
}

function normalize(value?: string | null): string {
  return (value ?? '')
    .toLowerCase()
    .replace(/[([{].*?(unabridged|abridged).*?[)\]}]/g, ' ')
    .replace(/[^\p{L}\p{N}]+/gu, ' ')
    .trim()
}

function tokenOverlap(a: string, b: string): number {
  const ta = new Set(a.split(' ').filter(Boolean))
  const tb = new Set(b.split(' ').filter(Boolean))
  if (ta.size === 0 || tb.size === 0) return 0
  let shared = 0
  for (const t of ta) if (tb.has(t)) shared++
  return shared / Math.max(ta.size, tb.size)
}

function authorsAgree(a: string, b: string): boolean {
  const ta = a.split(' ').filter((t) => t.length > 1)
  const tb = b.split(' ').filter((t) => t.length > 1)
  if (ta.length === 0 || tb.length === 0) return false
  return ta.every((t) => tb.includes(t)) || tb.every((t) => ta.includes(t))
}

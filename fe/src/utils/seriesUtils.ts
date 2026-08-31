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

type SeriesBearer = Pick<Audiobook, 'series' | 'seriesNumber' | 'seriesMemberships'>

/**
 * All series a book belongs to, formatted for display (e.g. "Publication Order #1,
 * Chronological Order #3"). Uses every series membership so a multi-series book shows all of
 * them; falls back to the legacy single series/number when no memberships are present.
 */
export function formatSeriesMemberships(book: SeriesBearer): string {
  const memberships = book.seriesMemberships
  if (memberships && memberships.length > 0) {
    const parts = memberships.map(formatMembership).filter(Boolean)
    if (parts.length > 0) return parts.join(', ')
  }

  const legacyName = (book.series || '').trim()
  if (!legacyName) return ''
  const legacyNumber = (book.seriesNumber || '').trim()
  return legacyNumber ? `${legacyName} #${legacyNumber}` : legacyName
}

function formatMembership(membership: AudiobookSeriesMembership): string {
  const name = (membership.seriesName || '').trim()
  if (!name) return ''
  const number = (membership.seriesNumber || '').trim()
  return number ? `${name} #${number}` : name
}

const POSITION_LABEL = /^(?:book|bk|volume|vol|part|episode|no|number)$/
const NUMBER_WORD =
  /^(?:one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen)$/

/**
 * Audible frequently sets a book's subtitle to nothing more than its series and
 * position - "Paragon Space, Book 1" for a book whose series badge already reads
 * "Paragon Space #1". Reports whether a subtitle is such a restatement, so a card
 * can drop it rather than print the series twice. A subtitle that adds anything
 * of its own ("A Heroic Saga") is not a restatement and must survive.
 */
export function isSeriesRestatement(subtitle: string, seriesNames: string[]): boolean {
  const subject = normalizeForComparison(subtitle)
  if (!subject) return false

  for (const seriesName of seriesNames) {
    const name = normalizeForComparison(seriesName)
    if (!name) continue

    // Audible writes the series with and without a trailing "Series", and either
    // form can reach the badge or the subtitle, so accept both as the same name.
    const bare = name.replace(/ series$/, '')
    for (const candidate of new Set([bare, `${bare} series`])) {
      if (subject === candidate) return true

      // "Paragon Space, Book 1" / "Paragon Space 1" / "Paragon Space Series"
      if (subject.startsWith(`${candidate} `)) {
        const rest = subject.slice(candidate.length + 1)
        if (rest === 'series' || isPositionPhrase(rest)) return true
      }

      // "Book 1 of Paragon Space"
      for (const suffix of [` of ${candidate}`, ` of the ${candidate}`]) {
        if (subject.endsWith(suffix) && isPositionPhrase(subject.slice(0, -suffix.length))) {
          return true
        }
      }
    }
  }

  return false
}

/** A bare position, with or without its label: "book 1", "vol 2", "3", "one". */
function isPositionPhrase(value: string): boolean {
  const tokens = value.split(' ').filter(Boolean)
  if (tokens.length === 2 && POSITION_LABEL.test(tokens[0])) tokens.shift()
  if (tokens.length !== 1) return false
  return /^\d+(?:\.\d+)?$/.test(tokens[0]) || NUMBER_WORD.test(tokens[0])
}

/**
 * Punctuation, case and spacing differ freely between a subtitle and a series
 * name that mean the same thing, so compare on letters and digits alone. Dots are
 * kept only inside a decimal, which is how a half-numbered entry ("1.5") is written.
 */
function normalizeForComparison(value: string): string {
  return (value || '')
    .toLowerCase()
    .replace(/[‘’']/g, '')
    .replace(/[^a-z0-9.]+/g, ' ')
    .replace(/\.(?!\d)/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

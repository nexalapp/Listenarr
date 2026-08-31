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
 * How much of a release date the metadata source actually committed to.
 *
 * Audible announces some titles as a month or a year. Rendering those as a specific
 * day invents information nobody published, and dropping them hides the release
 * entirely — so precision travels with the date and the UI shows what it was given.
 *
 * Mirrors `ReleaseDateWindow` on the backend; keep the two parsers in step.
 */
export type ReleaseDatePrecision = 'day' | 'month' | 'year'

export interface ParsedReleaseDate {
  precision: ReleaseDatePrecision
  /** Earliest day the window covers, as UTC midnight. */
  start: Date
  year: number
  /** 1-12, absent at year precision. */
  month?: number
  /** 1-31, absent at month and year precision. */
  day?: number
  /** Grouping key at the date's own precision: `2028-01-11`, `2028-01` or `2028`. */
  key: string
}

const DATE_PATTERN = /^(\d{4})(?:[-/](\d{1,2})(?:[-/](\d{1,2}))?)?$/

const MONTH_NAMES = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'May',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Oct',
  'Nov',
  'Dec',
]

const pad = (value: number): string => String(value).padStart(2, '0')

const daysInMonth = (year: number, month: number): number =>
  new Date(Date.UTC(year, month, 0)).getUTCDate()

/**
 * Reads a `publishedDate` into the window of time it covers, or null when the value
 * is missing or unreadable. Never guesses at a missing component.
 */
export function parseReleaseDate(value: string | null | undefined): ParsedReleaseDate | null {
  if (!value) return null

  // "2028-01-11T08:00:00Z" and "2028-01-11 08:00" both reduce to the date part.
  const trimmed = value.trim()
  const separatorIndex = trimmed.search(/[Tt ]/)
  const datePart = separatorIndex > 0 ? trimmed.slice(0, separatorIndex) : trimmed

  const match = DATE_PATTERN.exec(datePart)
  if (!match) return null

  const year = Number(match[1])
  if (!Number.isFinite(year) || year < 1 || year > 9999) return null

  if (match[2] === undefined) {
    return {
      precision: 'year',
      start: new Date(Date.UTC(year, 0, 1)),
      year,
      key: String(year).padStart(4, '0'),
    }
  }

  const month = Number(match[2])
  if (month < 1 || month > 12) return null

  if (match[3] === undefined) {
    return {
      precision: 'month',
      start: new Date(Date.UTC(year, month - 1, 1)),
      year,
      month,
      key: `${String(year).padStart(4, '0')}-${pad(month)}`,
    }
  }

  const day = Number(match[3])
  if (day < 1 || day > daysInMonth(year, month)) return null

  return {
    precision: 'day',
    start: new Date(Date.UTC(year, month - 1, day)),
    year,
    month,
    day,
    key: `${String(year).padStart(4, '0')}-${pad(month)}-${pad(day)}`,
  }
}

/**
 * Renders a date at its own precision: `Jan 11, 2028`, `Jan 2028` or `2028`.
 * A month-only date never grows a day it was not given.
 */
export function formatReleaseDate(parsed: ParsedReleaseDate | null): string {
  if (!parsed) return ''

  const monthName = parsed.month ? MONTH_NAMES[parsed.month - 1] : undefined

  switch (parsed.precision) {
    case 'day':
      return `${monthName} ${parsed.day}, ${parsed.year}`
    case 'month':
      return `${monthName} ${parsed.year}`
    case 'year':
      return String(parsed.year)
  }
}

/**
 * A longer form for empty-state and tooltip copy, where "sometime in" carries the
 * uncertainty that a bare `Jan 2028` leaves implicit.
 */
export function describeReleaseDate(parsed: ParsedReleaseDate | null): string {
  if (!parsed) return 'No release date'
  return parsed.precision === 'day'
    ? formatReleaseDate(parsed)
    : `Sometime in ${formatReleaseDate(parsed)}`
}

/**
 * True when even the earliest day the date could mean is still ahead of `today`.
 *
 * Comparing the start of the window rather than its end keeps a vague past date
 * ("2026", read in December 2026) out of the announced bucket: calling a book
 * unreleased when it is already out hides one the user could go and get.
 */
export function isFutureRelease(
  parsed: ParsedReleaseDate | null,
  today: Date = new Date(),
): boolean {
  if (!parsed) return false
  const todayUtc = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate())
  return parsed.start.getTime() > todayUtc
}

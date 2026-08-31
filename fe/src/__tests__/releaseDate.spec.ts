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
import { describe, it, expect } from 'vitest'
import {
  parseReleaseDate,
  formatReleaseDate,
  describeReleaseDate,
  isFutureRelease,
} from '@/utils/releaseDate'

describe('parseReleaseDate', () => {
  it('reads a full date at day precision', () => {
    const parsed = parseReleaseDate('2028-01-11')
    expect(parsed).not.toBeNull()
    expect(parsed!.precision).toBe('day')
    expect(parsed!.key).toBe('2028-01-11')
    expect(parsed!.start.toISOString()).toBe('2028-01-11T00:00:00.000Z')
  })

  it('strips a time component before parsing', () => {
    expect(parseReleaseDate('2028-01-11T08:00:00Z')!.key).toBe('2028-01-11')
    expect(parseReleaseDate('2028-01-11 08:00:00')!.key).toBe('2028-01-11')
  })

  it('keeps a month-only date as a month', () => {
    const parsed = parseReleaseDate('2028-03')
    expect(parsed!.precision).toBe('month')
    expect(parsed!.key).toBe('2028-03')
    expect(parsed!.day).toBeUndefined()
    expect(parsed!.start.toISOString()).toBe('2028-03-01T00:00:00.000Z')
  })

  it('keeps a year-only date as a year', () => {
    const parsed = parseReleaseDate('2028')
    expect(parsed!.precision).toBe('year')
    expect(parsed!.key).toBe('2028')
    expect(parsed!.month).toBeUndefined()
  })

  it.each([null, undefined, '', '   ', 'soon', '28-01-11', '2028-13-01', '2028-02-30'])(
    'refuses to guess at %s',
    (value) => {
      expect(parseReleaseDate(value as string | null | undefined)).toBeNull()
    },
  )

  it('accepts a leap day only in a leap year', () => {
    expect(parseReleaseDate('2028-02-29')).not.toBeNull()
    expect(parseReleaseDate('2027-02-29')).toBeNull()
  })
})

describe('formatReleaseDate', () => {
  it('renders each date at its own precision and never invents a day', () => {
    expect(formatReleaseDate(parseReleaseDate('2028-01-11'))).toBe('Jan 11, 2028')
    expect(formatReleaseDate(parseReleaseDate('2028-01'))).toBe('Jan 2028')
    expect(formatReleaseDate(parseReleaseDate('2028'))).toBe('2028')
  })

  it('spells the uncertainty out in the long form', () => {
    expect(describeReleaseDate(parseReleaseDate('2028-01-11'))).toBe('Jan 11, 2028')
    expect(describeReleaseDate(parseReleaseDate('2028-01'))).toBe('Sometime in Jan 2028')
    expect(describeReleaseDate(parseReleaseDate('2028'))).toBe('Sometime in 2028')
    expect(describeReleaseDate(null)).toBe('No release date')
  })
})

describe('isFutureRelease', () => {
  const today = new Date(2026, 7, 31) // 31 August 2026, local time

  it('treats a later day as still to come and today as not', () => {
    expect(isFutureRelease(parseReleaseDate('2028-01-11'), today)).toBe(true)
    expect(isFutureRelease(parseReleaseDate('2026-09-01'), today)).toBe(true)
    expect(isFutureRelease(parseReleaseDate('2026-08-31'), today)).toBe(false)
    expect(isFutureRelease(parseReleaseDate('2020-05-05'), today)).toBe(false)
  })

  it('compares the start of the window, so a vague past date is not announced', () => {
    // "2026" read in August 2026 could still mean December, but claiming the book is
    // unreleased would hide one the user can already go and get.
    expect(isFutureRelease(parseReleaseDate('2026'), today)).toBe(false)
    expect(isFutureRelease(parseReleaseDate('2026-08'), today)).toBe(false)
    expect(isFutureRelease(parseReleaseDate('2026-09'), today)).toBe(true)
    expect(isFutureRelease(parseReleaseDate('2027'), today)).toBe(true)
  })

  it('is false without a date', () => {
    expect(isFutureRelease(null, today)).toBe(false)
  })
})

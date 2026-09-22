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
import { describe, expect, it } from 'vitest'
import {
  createErrorToastGate,
  describeUnexpectedError,
  shouldReportUnexpectedError,
  unexpectedErrorToast,
} from '@/utils/unexpectedError'

describe('unexpectedError', () => {
  it('says nothing for a request the app cancelled', () => {
    expect(
      shouldReportUnexpectedError(new DOMException('The user aborted a request.', 'AbortError')),
    ).toBe(false)
    expect(shouldReportUnexpectedError({ name: 'AbortError', message: 'cancelled' })).toBe(false)
  })

  it('says nothing for a dropped connection, which the app recovers from itself', () => {
    expect(shouldReportUnexpectedError(new TypeError('Failed to fetch'))).toBe(false)
    expect(
      shouldReportUnexpectedError(new TypeError('NetworkError when attempting to fetch resource.')),
    ).toBe(false)
    expect(shouldReportUnexpectedError(new TypeError('Load failed'))).toBe(false)
  })

  it('says nothing for a chunk a deploy replaced, which reloads the page instead', () => {
    expect(
      shouldReportUnexpectedError(
        new TypeError('Failed to fetch dynamically imported module: /assets/CollectionView-abc.js'),
      ),
    ).toBe(false)
  })

  it('reports a real failure, and says what it was', () => {
    const error = new Error('The audiobook could not be deleted: the file is in use.')

    expect(shouldReportUnexpectedError(error)).toBe(true)
    expect(unexpectedErrorToast(error)).toEqual({
      title: 'Something went wrong',
      message: 'The audiobook could not be deleted: the file is in use.',
    })
  })

  it('reads the message off the shapes an API error arrives in', () => {
    expect(describeUnexpectedError({ message: 'Conflict' })).toBe('Conflict')
    expect(describeUnexpectedError({ detail: 'Destination is outside the library.' })).toBe(
      'Destination is outside the library.',
    )
    expect(describeUnexpectedError('plain string')).toBe('plain string')
    expect(describeUnexpectedError({ status: 500 })).toBe('')
  })

  it('points at the console when the error says nothing, rather than claiming to know', () => {
    expect(unexpectedErrorToast({ status: 500 })).toEqual({
      title: 'Something went wrong',
      message: 'The details are in the browser console.',
    })
  })

  it('trims a message too long for a toast', () => {
    const toast = unexpectedErrorToast(new Error('x'.repeat(400)))

    expect(toast.message).toHaveLength(200)
    expect(toast.message.endsWith('…')).toBe(true)
  })

  it('shows one toast per distinct failure per window, so a failing poller does not stack', () => {
    let now = 1_000
    const gate = createErrorToastGate(10_000, () => now)

    expect(gate('Server unavailable')).toBe(true)
    expect(gate('Server unavailable')).toBe(false)
    expect(gate('Something else')).toBe(true)

    now += 10_001
    expect(gate('Server unavailable')).toBe(true)
  })
})

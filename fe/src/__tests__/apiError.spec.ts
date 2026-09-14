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
import { describeApiError } from '@/utils/apiError'

/** What ApiService.request throws: a wrapper message plus the raw body. */
const apiError = (status: number, body: string) =>
  Object.assign(new Error(`API error: ${status} ${body}`), { status, body })

describe('describeApiError', () => {
  it('prefers the actionable detail a ProblemDetails carries', () => {
    const error = apiError(
      409,
      JSON.stringify({
        title: 'Cannot send to a download client',
        detail: 'No NZB download client is enabled, so this release cannot be sent anywhere.',
        code: 'download_client_unavailable',
      }),
    )

    expect(describeApiError(error)).toBe(
      'No NZB download client is enabled, so this release cannot be sent anywhere.',
    )
  })

  it('never puts the raw wrapper on screen', () => {
    // The whole reason this exists: users were shown
    // `API error: 500 {"title":"Internal server error",...,"traceId":"00-513f..."}`.
    const error = apiError(
      500,
      JSON.stringify({
        title: 'Internal server error',
        status: 500,
        instance: '/api/v1/download/send',
        code: 'internal_error',
        traceId: '00-513f7198404745cc336215b0d212c9f9-9cd46db319de5e0c-00',
      }),
    )

    const described = describeApiError(error, 'Could not send it anywhere.')

    expect(described).toBe('Could not send it anywhere.')
    expect(described).not.toContain('API error')
    expect(described).not.toContain('traceId')
  })

  it('reads the plainer message and error shapes other endpoints use', () => {
    expect(describeApiError(apiError(400, JSON.stringify({ message: 'Bad input' })))).toBe(
      'Bad input',
    )
    expect(
      describeApiError(apiError(410, JSON.stringify({ message: 'Gone', error: 'It expired' }))),
    ).toBe('It expired')
  })

  it('shows a title only when it actually says something', () => {
    expect(describeApiError(apiError(503, JSON.stringify({ title: 'Indexer unreachable' })))).toBe(
      'Indexer unreachable',
    )
  })

  it('treats a non-JSON body as the sentence it usually is', () => {
    expect(describeApiError(apiError(502, 'upstream timed out'))).toBe('upstream timed out')
  })

  it('keeps an error thrown before the request left the browser', () => {
    const local = new Error('This search result has expired. Run the search again.')

    expect(describeApiError(local)).toBe('This search result has expired. Run the search again.')
  })

  it('falls back when there is nothing usable at all', () => {
    expect(describeApiError(undefined, 'Nothing worked.')).toBe('Nothing worked.')
    expect(describeApiError(apiError(500, ''), 'Nothing worked.')).toBe('Nothing worked.')
    expect(describeApiError(apiError(500, '{}'), 'Nothing worked.')).toBe('Nothing worked.')
  })
})

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
 * The sentence to put in front of someone when an API call failed.
 *
 * `ApiService.request` throws `API error: <status> <raw body>`, so showing
 * `err.message` puts a status code and a blob of JSON on screen - which is what a
 * misconfigured download client looked like:
 *
 *   Download failed: API error: 500 {"title":"Internal server error","status":500,
 *   "instance":"/api/v1/download/send","code":"internal_error","traceId":"00-513f..."}
 *
 * The readable sentence, when the server sent one, is inside that body. Fields are
 * tried most-specific first: ProblemDetails `detail` carries the actionable reason,
 * `error` and `message` are the plainer shapes used elsewhere, and `title` is the
 * category - worth showing only if nothing better exists.
 */
const MESSAGE_FIELDS = ['detail', 'error', 'message', 'title'] as const

type ApiErrorLike = {
  message?: string
  status?: number
  body?: unknown
}

function readBody(body: unknown): Record<string, unknown> | null {
  if (!body) return null
  if (typeof body === 'object') return body as Record<string, unknown>
  if (typeof body !== 'string') return null

  try {
    const parsed = JSON.parse(body)
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : null
  } catch {
    // A non-JSON body is often a plain sentence already, and better than nothing.
    const text = body.trim()
    return text ? { detail: text } : null
  }
}

/**
 * Extract what a person should read from an error thrown by `ApiService`.
 *
 * Returns `fallback` when the server said nothing usable, rather than the raw
 * `API error: 500 {...}` string, which tells the reader nothing they can act on.
 */
export function describeApiError(error: unknown, fallback = 'Something went wrong.'): string {
  const candidate = error as ApiErrorLike | undefined
  const parsed = readBody(candidate?.body)

  if (parsed) {
    for (const field of MESSAGE_FIELDS) {
      const value = parsed[field]
      if (typeof value === 'string' && value.trim()) {
        // "Internal server error" is the filter's placeholder for a 5xx with nothing
        // to say. Showing it is no better than the fallback and reads like a leak.
        if (field === 'title' && value.trim() === 'Internal server error') continue
        return value.trim()
      }
    }
  }

  // An Error thrown before the request left the browser carries its own sentence, and
  // it is a real one - unlike the "API error: <status> <body>" wrapper.
  const message = candidate?.message
  if (typeof message === 'string' && message.trim() && !message.startsWith('API error:')) {
    return message.trim()
  }

  return fallback
}

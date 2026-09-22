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
 * What the global handlers do with an error nothing else caught.
 *
 * The old answer was one toast — "An unexpected error occurred" — for every
 * rejection in the app, which named nothing and so could not be acted on or
 * reported. This decides two things instead: whether the error is worth telling
 * anyone about, and what to say when it is.
 */

/** A request the app itself cancelled: navigating away, a newer search superseding this one. */
function isAborted(error: unknown): boolean {
  if (error instanceof DOMException && error.name === 'AbortError') return true
  return typeof error === 'object' && error !== null && 'name' in error
    ? (error as { name?: unknown }).name === 'AbortError' ||
        (error as { name?: unknown }).name === 'CanceledError'
    : false
}

/**
 * The server was not reachable for this request. Routine here: the app is deployed
 * by restarting the container, and a tab open across a restart loses whatever was
 * in flight. The app recovers on its own — SignalR reconnects, pollers poll again —
 * so a toast per dropped request is noise, not news.
 */
function isNetworkBlip(message: string): boolean {
  return /failed to fetch|networkerror|network request failed|load failed|err_network|err_connection|the network connection was lost/i.test(
    message,
  )
}

/** A deploy replaced the bundle under this tab; the app reloads itself for these. */
function isChunkLoad(message: string): boolean {
  return /failed to fetch dynamically imported module|importing a module script failed|error loading dynamically imported module|unable to preload css/i.test(
    message,
  )
}

export function describeUnexpectedError(error: unknown): string {
  if (error instanceof Error) return error.message || error.name
  if (typeof error === 'string') return error
  if (typeof error === 'object' && error !== null) {
    const candidate = error as { message?: unknown; detail?: unknown; title?: unknown }
    for (const value of [candidate.message, candidate.detail, candidate.title]) {
      if (typeof value === 'string' && value.trim().length > 0) return value.trim()
    }
  }

  return ''
}

/** Whether this error is worth a toast at all. */
export function shouldReportUnexpectedError(error: unknown): boolean {
  if (isAborted(error)) return false

  const message = describeUnexpectedError(error)
  return !isNetworkBlip(message) && !isChunkLoad(message)
}

/**
 * The toast for an error that got this far: its own words, trimmed to a line, so a
 * person can say what went wrong rather than only that something did.
 */
export function unexpectedErrorToast(error: unknown): { title: string; message: string } {
  const message = describeUnexpectedError(error).replace(/\s+/g, ' ').trim()
  if (!message) {
    return { title: 'Something went wrong', message: 'The details are in the browser console.' }
  }

  return {
    title: 'Something went wrong',
    message: message.length > 200 ? `${message.slice(0, 199)}…` : message,
  }
}

/**
 * One toast per distinct error per window. A failing poller repeats every second or
 * two, and a stack of identical toasts buries whatever else the app is saying.
 */
export function createErrorToastGate(windowMs = 10_000, now: () => number = Date.now) {
  const lastShown = new Map<string, number>()

  return (message: string): boolean => {
    const at = now()
    for (const [key, shown] of lastShown) {
      if (at - shown > windowMs) lastShown.delete(key)
    }

    if (lastShown.has(message)) return false
    lastShown.set(message, at)
    return true
  }
}

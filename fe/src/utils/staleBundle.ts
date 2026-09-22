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
 * Reload once when a route's chunk is missing.
 *
 * A deploy replaces the hashed bundle files, so a tab that loaded the app before it
 * asks for chunks that no longer exist the first time it opens a page it has not
 * visited: "Failed to fetch dynamically imported module". That is not an error the
 * person can act on beyond refreshing, so the app refreshes for them — once. The
 * guard is keyed on the URL and kept in sessionStorage, so a chunk that is missing
 * for some other reason (the server is down, a broken build) shows the error rather
 * than reloading forever.
 */

const GUARD_KEY = 'listenarr:stale-bundle-reload'

const CHUNK_LOAD_FAILURE =
  /Failed to fetch dynamically imported module|Importing a module script failed|error loading dynamically imported module|Unable to preload CSS/i

export function isChunkLoadError(error: unknown): boolean {
  const message =
    error instanceof Error
      ? error.message
      : typeof error === 'string'
        ? error
        : error && typeof error === 'object' && 'message' in error
          ? String((error as { message: unknown }).message)
          : ''
  return CHUNK_LOAD_FAILURE.test(message)
}

type ReloadTarget = {
  location: { href: string; reload: () => void; assign: (url: string) => void }
  sessionStorage?: Pick<Storage, 'getItem' | 'setItem'>
}

/**
 * Load the page fresh for a missing chunk, unless this URL already got its one
 * reload. With `href`, the fresh load goes there: a navigation whose destination
 * failed to load has not changed the address bar yet, and reloading it would only
 * land back where the person already was. Returns whether a load was started, so
 * the caller can leave the error unreported.
 */
export function reloadForStaleBundle(target: ReloadTarget, href?: string): boolean {
  const destination = href ?? target.location.href
  let guarded: string | null = null
  try {
    guarded = target.sessionStorage?.getItem(GUARD_KEY) ?? null
  } catch {
    // Private mode or blocked storage: without a guard a loop is possible, so do not reload.
    return false
  }

  if (guarded === destination) return false

  try {
    target.sessionStorage?.setItem(GUARD_KEY, destination)
  } catch {
    return false
  }

  if (href) {
    target.location.assign(href)
  } else {
    target.location.reload()
  }
  return true
}

/** A page that loaded after a reload can be reloaded again for a later deploy. */
export function clearStaleBundleGuard(storage: Pick<Storage, 'removeItem'> | undefined): void {
  try {
    storage?.removeItem(GUARD_KEY)
  } catch {
    // Nothing to clear.
  }
}

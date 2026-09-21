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
import { describe, expect, it, vi } from 'vitest'
import { clearStaleBundleGuard, isChunkLoadError, reloadForStaleBundle } from '@/utils/staleBundle'

function fakeWindow(href = 'http://app/books') {
  const store = new Map<string, string>()
  return {
    location: { href, reload: vi.fn(), assign: vi.fn() },
    sessionStorage: {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => void store.set(key, value),
      removeItem: (key: string) => void store.delete(key),
    },
  }
}

describe('staleBundle', () => {
  it('recognises the errors a replaced bundle produces', () => {
    expect(
      isChunkLoadError(
        new TypeError(
          'Failed to fetch dynamically imported module: http://app/assets/CollectionView-abc.js',
        ),
      ),
    ).toBe(true)
    expect(isChunkLoadError(new TypeError('Importing a module script failed.'))).toBe(true)
    expect(isChunkLoadError({ message: 'error loading dynamically imported module' })).toBe(true)
    expect(isChunkLoadError(new Error('Request failed with status 500'))).toBe(false)
    expect(isChunkLoadError(undefined)).toBe(false)
  })

  it('reloads once for a URL and then leaves the error alone', () => {
    const w = fakeWindow()

    expect(reloadForStaleBundle(w)).toBe(true)
    expect(w.location.reload).toHaveBeenCalledTimes(1)

    expect(reloadForStaleBundle(w)).toBe(false)
    expect(w.location.reload).toHaveBeenCalledTimes(1)
  })

  it('goes to the destination that failed to load rather than reloading the page it left', () => {
    const w = fakeWindow()

    expect(reloadForStaleBundle(w, '/collection/series/Apocalypse%20Triptych')).toBe(true)
    expect(w.location.assign).toHaveBeenCalledWith('/collection/series/Apocalypse%20Triptych')
    expect(w.location.reload).not.toHaveBeenCalled()

    // The same destination failing again after the fresh load is a real problem, not staleness.
    expect(reloadForStaleBundle(w, '/collection/series/Apocalypse%20Triptych')).toBe(false)
    expect(w.location.assign).toHaveBeenCalledTimes(1)
  })

  it('reloads again for a later deploy once a route has loaded in between', () => {
    const w = fakeWindow()
    expect(reloadForStaleBundle(w)).toBe(true)

    clearStaleBundleGuard(w.sessionStorage)

    expect(reloadForStaleBundle(w)).toBe(true)
    expect(w.location.reload).toHaveBeenCalledTimes(2)
  })

  it('does not reload when storage cannot hold the guard, so it cannot loop', () => {
    const w = fakeWindow()
    w.sessionStorage.getItem = () => {
      throw new Error('blocked')
    }

    expect(reloadForStaleBundle(w)).toBe(false)
    expect(w.location.reload).not.toHaveBeenCalled()
  })
})

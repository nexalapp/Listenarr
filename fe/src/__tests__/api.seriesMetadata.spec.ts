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
import { describe, it, expect, vi, afterEach } from 'vitest'

// A caller deciding whether a series exists on Audible has to be able to tell "Audible says
// no" from "we could not ask" — collapsing both to null withdraws features on a blip.
describe('ApiService series metadata lookups', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  const stubFetch = (status: number) => {
    const fetchMock = vi.fn(() =>
      Promise.resolve(
        new Response(status === 404 ? 'Series not found' : 'boom', {
          status,
          headers: { 'Content-Type': 'text/plain' },
        }),
      ),
    )
    vi.stubGlobal('fetch', fetchMock)
    return fetchMock
  }

  it('returns null when Audible has no such series', async () => {
    vi.resetModules()
    stubFetch(404)

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')

    await expect(actual.apiService.getSeriesCatalog('Homemade Saga')).resolves.toBeNull()
    await expect(actual.apiService.getSeriesLookup('Homemade Saga')).resolves.toBeNull()
  })

  it('propagates a failed request instead of reporting it as not found', async () => {
    vi.resetModules()
    stubFetch(500)

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')

    await expect(actual.apiService.getSeriesCatalog('Mistborn')).rejects.toThrow()
    await expect(actual.apiService.getSeriesLookup('Mistborn')).rejects.toThrow()
  })
})

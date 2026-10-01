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
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'

vi.mock('@/services/toastService', () => ({
  useToast: () => ({ info: vi.fn(), success: vi.fn(), error: vi.fn() }),
}))

/**
 * The query string is the whole contract between the button and the walk. Nothing else in
 * the app can tell an ordinary audit from one asked to keep looking, and a silent default
 * back to the short walk is exactly the kind of regression that only shows up as a book
 * whose credits are never found.
 */
describe('ApiService audit', () => {
  let apiService: {
    auditAudio: (id: number, listenFurther?: boolean) => Promise<unknown>
  }
  let urls: string[]
  let originalFetch: typeof globalThis.fetch

  beforeEach(async () => {
    urls = []
    originalFetch = globalThis.fetch
    globalThis.fetch = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      urls.push(url)
      if (url.endsWith('/antiforgery/token')) {
        return Promise.resolve(
          new Response(JSON.stringify({ token: 't' }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        )
      }

      return Promise.resolve(
        new Response(JSON.stringify({ queued: true, jobId: 'j1' }), {
          status: 202,
          headers: { 'Content-Type': 'application/json' },
        }),
      )
    }) as unknown as typeof globalThis.fetch

    // The real client, not whatever another spec left mocked in the module registry.
    vi.resetModules()
    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')
    apiService = actual.apiService as unknown as typeof apiService
  })

  afterEach(() => {
    globalThis.fetch = originalFetch
    vi.restoreAllMocks()
  })

  it('asks for an ordinary listen by default', async () => {
    await apiService.auditAudio(756)

    const audit = urls.filter((url) => url.includes('/audit'))
    expect(audit.length).toBeGreaterThan(0)
    expect(audit.every((url) => !url.includes('listenFurther'))).toBe(true)
  })

  it('carries listenFurther when asked to listen further', async () => {
    await apiService.auditAudio(756, true)

    expect(urls.some((url) => url.includes('/audit?listenFurther=true'))).toBe(true)
  })
})

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
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { AutoGrabSettings } from '@/types'

const api = vi.hoisted(() => ({
  getAutoGrabSettings: vi.fn(),
  saveAutoGrabSettings: vi.fn(),
  getAutoGrabPreview: vi.fn(),
  runAutoGrab: vi.fn(),
}))

vi.mock('@/services/api', () => ({ apiService: api }))

import { useAutoGrabStore } from '@/stores/autoGrab'

const off: AutoGrabSettings = { enabled: false, perRun: 10, intervalMinutes: 60, sources: [] }

describe('the auto-grab store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    api.saveAutoGrabSettings.mockImplementation(
      (settings: AutoGrabSettings) => Promise.resolve(settings),
    )
  })

  it('arms auto-grab when a list is ticked, since that is the only control on the page', async () => {
    api.getAutoGrabSettings.mockResolvedValue({ ...off })
    const store = useAutoGrabStore()

    await store.toggle('series')

    expect(api.saveAutoGrabSettings).toHaveBeenCalledWith(
      expect.objectContaining({
        enabled: true,
        sources: [{ kind: 'series', sectionId: null, enabled: true }],
      }),
    )
  })

  it('disarms it when the last ticked list is unticked', async () => {
    api.getAutoGrabSettings.mockResolvedValue({
      ...off,
      enabled: true,
      sources: [{ kind: 'series', sectionId: null, enabled: true }],
    })
    const store = useAutoGrabStore()

    await store.toggle('series')

    expect(api.saveAutoGrabSettings).toHaveBeenCalledWith(
      expect.objectContaining({ enabled: false }),
    )
  })

  it('stays armed while any other list is still ticked', async () => {
    api.getAutoGrabSettings.mockResolvedValue({
      ...off,
      enabled: true,
      sources: [
        { kind: 'series', sectionId: null, enabled: true },
        { kind: 'author', sectionId: null, enabled: true },
      ],
    })
    const store = useAutoGrabStore()

    await store.toggle('series')

    expect(api.saveAutoGrabSettings).toHaveBeenCalledWith(
      expect.objectContaining({ enabled: true }),
    )
  })
})

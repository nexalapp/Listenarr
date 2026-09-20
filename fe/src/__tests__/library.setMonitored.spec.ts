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
import { setActivePinia, createPinia } from 'pinia'
import { describe, test, expect, beforeEach, vi } from 'vitest'
import type { Audiobook } from '@/types'

const updateAudiobook = vi.fn()

vi.mock('@/services/api', () => ({
  apiService: {
    updateAudiobook: (...args: unknown[]) => updateAudiobook(...args),
  },
}))

import { useLibraryStore } from '@/stores/library'

describe('library store setMonitored', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    updateAudiobook.mockReset()
  })

  test('shows the flip before the server answers and sends only the flag', async () => {
    const store = useLibraryStore()
    store.audiobooks = [{ id: 1, title: 'A', monitored: false }] as Audiobook[]
    let resolve: () => void = () => {}
    updateAudiobook.mockReturnValue(new Promise<void>((r) => (resolve = r)))

    const pending = store.setMonitored(1, true)
    expect(store.audiobooks[0]?.monitored).toBe(true)

    resolve()
    await pending
    expect(updateAudiobook).toHaveBeenCalledWith(1, { monitored: true })
    expect(store.audiobooks[0]?.monitored).toBe(true)
  })

  test('puts the flag back and rethrows when the server refuses', async () => {
    const store = useLibraryStore()
    store.audiobooks = [{ id: 1, title: 'A', monitored: true }] as Audiobook[]
    updateAudiobook.mockRejectedValue(new Error('nope'))

    await expect(store.setMonitored(1, false)).rejects.toThrow('nope')
    expect(store.audiobooks[0]?.monitored).toBe(true)
  })

  test('ignores a book that is not loaded', async () => {
    const store = useLibraryStore()
    store.audiobooks = []
    updateAudiobook.mockResolvedValue({})

    await store.setMonitored(99, true)
    expect(updateAudiobook).toHaveBeenCalledWith(99, { monitored: true })
    expect(store.audiobooks).toEqual([])
  })
})

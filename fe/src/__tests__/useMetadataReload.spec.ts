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

const rescanAudiobookMetadata = vi.fn()
const showConfirm = vi.fn()

vi.mock('@/services/api', () => ({ apiService: { rescanAudiobookMetadata } }))
vi.mock('@/composables/useConfirm', () => ({ showConfirm }))
vi.mock('@/services/toastService', () => ({
  useToast: () => ({ success: vi.fn(), warning: vi.fn(), info: vi.fn(), error: vi.fn() }),
}))

describe('useMetadataReload', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    rescanAudiobookMetadata.mockResolvedValue({})
    showConfirm.mockResolvedValue(true)
  })

  it('reloads a small batch without asking first', async () => {
    const { useMetadataReload } = await import('@/composables/useMetadataReload')
    const { requestReload } = useMetadataReload()

    await requestReload([
      { id: 1, title: 'One' },
      { id: 2, title: 'Two' },
    ])

    expect(showConfirm).not.toHaveBeenCalled()
    expect(rescanAudiobookMetadata).toHaveBeenCalledTimes(2)
  })

  it('asks before a batch large enough to be slow, and honours a cancel', async () => {
    const { useMetadataReload, METADATA_RELOAD_CONFIRM_THRESHOLD } =
      await import('@/composables/useMetadataReload')
    showConfirm.mockResolvedValue(false)
    const { requestReload } = useMetadataReload()

    const many = Array.from({ length: METADATA_RELOAD_CONFIRM_THRESHOLD + 1 }, (_, i) => ({
      id: i + 1,
    }))
    await requestReload(many)

    expect(showConfirm).toHaveBeenCalledTimes(1)
    // Cancelling must not fire a single provider request.
    expect(rescanAudiobookMetadata).not.toHaveBeenCalled()
  })

  it('skips catalogue-only entries, which carry synthetic negative ids', async () => {
    const { useMetadataReload } = await import('@/composables/useMetadataReload')
    const { requestReload } = useMetadataReload()

    await requestReload([
      { id: -475520451, title: 'Not in library' },
      { id: 7, title: 'Owned' },
      { id: 7, title: 'Owned duplicate' },
    ])

    expect(rescanAudiobookMetadata).toHaveBeenCalledTimes(1)
    expect(rescanAudiobookMetadata).toHaveBeenCalledWith(7)
  })

  it('keeps going when one book fails', async () => {
    const { useMetadataReload } = await import('@/composables/useMetadataReload')
    rescanAudiobookMetadata
      .mockRejectedValueOnce(new Error('429 Too Many Requests'))
      .mockResolvedValue({})
    const { requestReload } = useMetadataReload()

    await requestReload([{ id: 1 }, { id: 2 }, { id: 3 }])

    // A throttled book must not abandon the rest of the batch.
    expect(rescanAudiobookMetadata).toHaveBeenCalledTimes(3)
  })
})

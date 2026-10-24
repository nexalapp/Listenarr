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
import { mount } from '@vue/test-utils'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import RepairPreviewModal from '@/components/domain/repair/RepairPreviewModal.vue'
import { apiService } from '@/services/api'
import AudioPreviewPlayer from '@/components/ui/AudioPreviewPlayer.vue'

vi.mock('@/services/api', () => ({
  apiService: {
    startRepairPreview: vi.fn(async () => ({
      previewId: 'p1',
      audiobookId: 5,
      state: 'Pending',
      detail: 'Waiting for the enhancer.',
      ready: false,
    })),
    getRepairPreview: vi.fn(async () => ({
      previewId: 'p1',
      audiobookId: 5,
      state: 'Ready',
      detail: null,
      ready: true,
    })),
    buildRepairPreviewClipUrl: vi.fn(
      (id: string, variant: string) => `/api/v1/repair/previews/${id}/clip?variant=${variant}`,
    ),
    repairAudio: vi.fn(async () => ({ queued: true, jobId: 'j1' })),
  },
}))

describe('RepairPreviewModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.useFakeTimers()
  })

  async function open() {
    const wrapper = mount(RepairPreviewModal, {
      props: { visible: false, audiobookId: 5 },
    })
    await wrapper.setProps({ visible: true })
    await Promise.resolve()
    await Promise.resolve()
    return wrapper
  }

  it('starts a preview when opened and keeps the accept button off until it is ready', async () => {
    const wrapper = await open()

    expect(apiService.startRepairPreview).toHaveBeenCalledWith(5)
    const accept = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Repair the whole book'))
    expect(accept).toBeTruthy()
    expect(accept!.attributes('disabled')).toBeDefined()
  })

  it('polls to ready and offers both clips through the identical encode', async () => {
    const wrapper = await open()

    await vi.advanceTimersByTimeAsync(3100)
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(apiService.getRepairPreview).toHaveBeenCalledWith('p1')
    // The player renders no audio element until it is played; the clip URLs on the two
    // players' props are what prove both sides exist and go through the same endpoint.
    const sources = wrapper
      .findAllComponents(AudioPreviewPlayer)
      .map((player) => player.props('src'))
    expect(sources).toContain('/api/v1/repair/previews/p1/clip?variant=before')
    expect(sources).toContain('/api/v1/repair/previews/p1/clip?variant=after')

    const accept = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Repair the whole book'))
    expect(accept!.attributes('disabled')).toBeUndefined()
  })

  it('queues the repair on accept and tells the caller', async () => {
    const wrapper = await open()
    await vi.advanceTimersByTimeAsync(3100)
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    const accept = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Repair the whole book'))
    await accept!.trigger('click')
    await Promise.resolve()

    expect(apiService.repairAudio).toHaveBeenCalledWith(5)
    expect(wrapper.emitted('queued')).toBeTruthy()
    expect(wrapper.emitted('close')).toBeTruthy()
  })
})

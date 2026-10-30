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

const apiMocks = vi.hoisted(() => ({
  getConversionJobs: vi.fn(),
}))

const signalRMocks = vi.hoisted(() => {
  const state = {
    update: null as ((job: Record<string, unknown>) => void) | null,
    connected: null as (() => void) | null,
    offUpdate: vi.fn(),
    offConnected: vi.fn(),
    onConversionJobUpdate: vi.fn(),
    onConnected: vi.fn(),
  }
  state.onConversionJobUpdate.mockImplementation((cb: (job: Record<string, unknown>) => void) => {
    state.update = cb
    return state.offUpdate
  })
  state.onConnected.mockImplementation((cb: () => void) => {
    state.connected = cb
    return state.offConnected
  })
  return state
})

vi.mock('@/services/api', () => ({
  apiService: { getConversionJobs: apiMocks.getConversionJobs },
}))

vi.mock('@/services/signalr', () => ({
  signalRService: {
    onConversionJobUpdate: signalRMocks.onConversionJobUpdate,
    onConnected: signalRMocks.onConnected,
  },
}))

import { useConversionJobsStore } from '@/stores/conversionJobs'

const running = { jobId: 'job-1', audiobookId: 7, status: 'Running', phase: 'Encoding', kind: 'Repair', progress: 90 }

describe('conversion jobs store', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
  })

  it('re-reads the jobs on reconnect, so a job that finished while disconnected stops showing as running', async () => {
    apiMocks.getConversionJobs.mockResolvedValueOnce([running])
    const store = useConversionJobsStore()
    store.start()
    await vi.waitFor(() => expect(store.activeJobs).toHaveLength(1))

    // The Completed update was broadcast while the socket was down and is never replayed.
    apiMocks.getConversionJobs.mockResolvedValueOnce([{ ...running, status: 'Completed', phase: 'None' }])
    signalRMocks.connected?.()

    await vi.waitFor(() => expect(store.activeJobs).toHaveLength(0))
    expect(store.jobs[0]?.status).toBe('Completed')
  })

  it('stops listening for reconnects when stopped', () => {
    apiMocks.getConversionJobs.mockResolvedValue([])
    const store = useConversionJobsStore()
    store.start()
    store.stop()

    expect(signalRMocks.offUpdate).toHaveBeenCalledOnce()
    expect(signalRMocks.offConnected).toHaveBeenCalledOnce()
  })
})

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
import { setActivePinia, createPinia } from 'pinia'

const HOUR = 60 * 60 * 1000

const status = (overrides: Record<string, unknown> = {}) => ({
  configured: true,
  estimatedBalance: 40,
  maxTokens: 100,
  reserveFloor: 5,
  spendable: 35,
  nextRefillAt: new Date(Date.now() + 20 * 60 * 1000).toISOString(),
  lastSuccessfulUseAt: null,
  keyDeleted: false,
  summary: '',
  spentRecently: 0,
  refusedRecently: 0,
  ...overrides,
})

const mockApi = (payload: Record<string, unknown>) => {
  vi.doMock('@/services/api', () => ({
    apiService: { getNzbKingStatus: vi.fn(async () => payload) },
  }))
}

const loadStore = async () => {
  const { useNzbKingTokensStore } = await import('@/stores/nzbKingTokens')
  const store = useNzbKingTokensStore()
  await store.load()
  return store
}

describe('nzbKingTokens store', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.useFakeTimers()
    setActivePinia(createPinia())
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('counts refills locally instead of asking the server again', async () => {
    // The balance is a pure function of a stored figure and a known hourly schedule,
    // which is why this needs no polling and no socket of its own.
    mockApi(
      status({
        estimatedBalance: 40,
        nextRefillAt: new Date(Date.now() + 20 * 60 * 1000).toISOString(),
      }),
    )
    const store = await loadStore()

    expect(store.estimatedBalance).toBe(40)

    // Past the first refill, then two more hours.
    vi.setSystemTime(Date.now() + 20 * 60 * 1000 + 2 * HOUR + 1000)
    store.start()
    await vi.advanceTimersByTimeAsync(60_000)

    expect(store.estimatedBalance).toBe(43)
    store.stop()
  })

  it('never reports more than the maximum however long it sits idle', async () => {
    mockApi(
      status({ estimatedBalance: 98, nextRefillAt: new Date(Date.now() + 1000).toISOString() }),
    )
    const store = await loadStore()

    vi.setSystemTime(Date.now() + 500 * HOUR)
    store.start()
    await vi.advanceTimersByTimeAsync(60_000)

    expect(store.estimatedBalance).toBe(100)
    expect(store.msUntilNextRefill).toBeNull()
    store.stop()
  })

  it('reports what is spendable above the reserve, not the raw balance', async () => {
    // Spending into the reserve is how the key gets deleted, so the reserve is not
    // part of what anyone may spend.
    mockApi(status({ estimatedBalance: 8, reserveFloor: 5, nextRefillAt: null }))
    const store = await loadStore()

    expect(store.estimatedBalance).toBe(8)
    expect(store.spendable).toBe(3)
    expect(store.needsAttention).toBe(false)
  })

  it('asks for attention once nothing may be spent', async () => {
    mockApi(status({ estimatedBalance: 5, reserveFloor: 5, nextRefillAt: null }))
    const store = await loadStore()

    expect(store.spendable).toBe(0)
    expect(store.needsAttention).toBe(true)
  })

  it('asks for attention when the key is gone, and accrues nothing further', async () => {
    // A deleted key does not refill: it no longer exists.
    mockApi(
      status({
        estimatedBalance: 0,
        keyDeleted: true,
        nextRefillAt: new Date(Date.now() - HOUR).toISOString(),
      }),
    )
    const store = await loadStore()

    vi.setSystemTime(Date.now() + 5 * HOUR)
    store.start()
    await vi.advanceTimersByTimeAsync(60_000)

    expect(store.estimatedBalance).toBe(0)
    expect(store.needsAttention).toBe(true)
    store.stop()
  })

  it('stays silent when no key is configured', async () => {
    mockApi({
      configured: false,
      maxTokens: 100,
      reserveFloor: 5,
      summary: '',
      spentRecently: 0,
      refusedRecently: 0,
    })
    const store = await loadStore()

    expect(store.needsAttention).toBe(false)
    expect(store.estimatedBalance).toBe(0)
  })
})

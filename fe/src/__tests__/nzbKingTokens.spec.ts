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

const key = (overrides: Record<string, unknown> = {}) => ({
  label: 'aaaaaaaa',
  estimatedBalance: 40,
  spendable: 35,
  nextRefillAt: new Date(Date.now() + 20 * 60 * 1000).toISOString(),
  lastSuccessfulUseAt: null,
  keyDeleted: false,
  ...overrides,
})

/**
 * A pool of one, which is what a single configured key is. The per-key figures mirror
 * the pooled ones so a test can set either; `keys` is what the store actually reads,
 * because refills land per key.
 */
const status = (overrides: Record<string, unknown> = {}) => {
  const merged = {
    configured: true,
    estimatedBalance: 40,
    maxTokens: 100,
    reserveFloor: 5,
    spendable: 35,
    keyCount: 1,
    deletedKeyCount: 0,
    nextRefillAt: new Date(Date.now() + 20 * 60 * 1000).toISOString(),
    lastSuccessfulUseAt: null,
    keyDeleted: false,
    summary: '',
    refusedRecently: 0,
    ...overrides,
  } as Record<string, unknown>

  return {
    ...merged,
    keys: (merged.keys as unknown[]) ?? [
      key({
        estimatedBalance: merged.estimatedBalance,
        nextRefillAt: merged.nextRefillAt,
        keyDeleted: merged.keyDeleted,
      }),
    ],
    deletedKeyCount: merged.keyDeleted ? 1 : (merged.deletedKeyCount as number),
  }
}

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
      keyCount: 0,
      deletedKeyCount: 0,
      keys: [],
      summary: '',
      refusedRecently: 0,
    })
    const store = await loadStore()

    expect(store.needsAttention).toBe(false)
    expect(store.estimatedBalance).toBe(0)
  })
  it('counts a refill for every key below its cap, not one for the pool', async () => {
    // The bug this guards: adding one token an hour to a pooled balance. Each key earns
    // its own token, so five keys below their caps earn five an hour -- a pool that
    // accrued like a single key would under-report by a factor of its size.
    const due = new Date(Date.now() + 20 * 60 * 1000).toISOString()
    mockApi(
      status({
        keyCount: 3,
        keys: [
          key({ label: 'aaaaaaaa', estimatedBalance: 40, nextRefillAt: due }),
          key({ label: 'bbbbbbbb', estimatedBalance: 30, nextRefillAt: due }),
          key({ label: 'cccccccc', estimatedBalance: 20, nextRefillAt: due }),
        ],
      }),
    )
    const store = await loadStore()

    expect(store.estimatedBalance).toBe(90)
    expect(store.spendable).toBe(90 - 3 * 5)

    vi.setSystemTime(Date.now() + 20 * 60 * 1000 + 2 * HOUR + 1000)
    store.start()
    await vi.advanceTimersByTimeAsync(60_000)

    // Three tokens an hour for three hours of elapsed refills, not three.
    expect(store.estimatedBalance).toBe(90 + 9)
    store.stop()
  })

  it('does not let a full key lend its headroom to an empty one', async () => {
    // Capping the pooled balance at the pooled ceiling would allow it: a key at 100
    // cannot hold a 101st token however little the others hold.
    const due = new Date(Date.now() + 1000).toISOString()
    mockApi(
      status({
        keyCount: 2,
        keys: [
          key({ label: 'aaaaaaaa', estimatedBalance: 99, nextRefillAt: due }),
          key({ label: 'bbbbbbbb', estimatedBalance: 10, nextRefillAt: due }),
        ],
      }),
    )
    const store = await loadStore()

    vi.setSystemTime(Date.now() + 500 * HOUR)
    store.start()
    await vi.advanceTimersByTimeAsync(60_000)

    // 100 for the first key, 100 for the second: neither goes past its own cap.
    expect(store.estimatedBalance).toBe(200)
    expect(store.msUntilNextRefill).toBeNull()
    store.stop()
  })

  it('holds a reserve back on every key, not once for the pool', async () => {
    // A key at its reserve cannot be helped by another key's balance, so the reserve is
    // owed per key: three keys of 8 can spend 9, not 19.
    mockApi(
      status({
        keyCount: 3,
        keys: [
          key({ label: 'aaaaaaaa', estimatedBalance: 8, nextRefillAt: null }),
          key({ label: 'bbbbbbbb', estimatedBalance: 8, nextRefillAt: null }),
          key({ label: 'cccccccc', estimatedBalance: 8, nextRefillAt: null }),
        ],
      }),
    )
    const store = await loadStore()

    expect(store.estimatedBalance).toBe(24)
    expect(store.spendable).toBe(9)
  })

  it('counts nothing from a deleted key but still asks for attention', async () => {
    // A dead key among live ones is easy to miss: the pool goes on working, smaller,
    // and it will not come back without a person solving a CAPTCHA.
    mockApi(
      status({
        keyCount: 2,
        deletedKeyCount: 1,
        keys: [
          key({ label: 'aaaaaaaa', estimatedBalance: 60, nextRefillAt: null }),
          key({ label: 'bbbbbbbb', estimatedBalance: 0, keyDeleted: true, nextRefillAt: null }),
        ],
      }),
    )
    const store = await loadStore()

    expect(store.estimatedBalance).toBe(60)
    expect(store.spendable).toBe(55)
    expect(store.poolMaxTokens).toBe(100)
    expect(store.needsAttention).toBe(true)
  })

  it('waits for the earliest refill in the pool', async () => {
    mockApi(
      status({
        keyCount: 2,
        keys: [
          key({
            label: 'aaaaaaaa',
            estimatedBalance: 40,
            nextRefillAt: new Date(Date.now() + 50 * 60 * 1000).toISOString(),
          }),
          key({
            label: 'bbbbbbbb',
            estimatedBalance: 40,
            nextRefillAt: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
          }),
        ],
      }),
    )
    const store = await loadStore()

    expect(store.msUntilNextRefill).toBe(10 * 60 * 1000)
  })
})

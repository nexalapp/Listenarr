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
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { apiService } from '@/services/api'
import type { NzbKingStatus } from '@/types'
import { logger } from '@/utils/logger'

/**
 * NZBKing's token allowance.
 *
 * The balance needs no polling and no socket of its own. It is a pure function of
 * two stored values — a balance and the moment the next token is due — and tokens
 * return on a fixed hourly schedule, so the count between fetches is arithmetic the
 * client can do itself. The only unpredictable change is a spend, and a spend only
 * happens because this application grabbed something, which already announces
 * itself over the existing SignalR connection.
 *
 * So: fetch on demand, count refills locally, and refetch when told something was
 * spent. Nothing here runs on a timer against the server.
 */
export const useNzbKingTokensStore = defineStore('nzbKingTokens', () => {
  const status = ref<NzbKingStatus | null>(null)
  const isLoading = ref(false)
  const lastFetchedAt = ref<number | null>(null)

  // Advanced by a ticking clock so the derived values recompute; the refill maths
  // below depends on the current time, which is not otherwise reactive.
  const now = ref(Date.now())
  let clock: ReturnType<typeof setInterval> | null = null

  const REFILL_INTERVAL_MS = 60 * 60 * 1000

  const load = async (force = false) => {
    // The figures only move on the hour, or when this app spends. Re-fetching more
    // often than that just adds requests without adding information.
    if (!force && lastFetchedAt.value && Date.now() - lastFetchedAt.value < 30_000) {
      return
    }

    isLoading.value = true
    try {
      status.value = await apiService.getNzbKingStatus()
      lastFetchedAt.value = Date.now()
    } catch (error) {
      logger.warn('[NZBKing] Failed to load token status', error)
    } finally {
      isLoading.value = false
    }
  }

  /**
   * How many tokens have accrued since the server told us the balance. Refills land
   * one per hour on a known schedule, so this is derived rather than requested.
   */
  const accrued = computed(() => {
    const s = status.value
    if (!s?.configured || s.keyDeleted || !s.nextRefillAt) return 0

    const due = new Date(s.nextRefillAt).getTime()
    if (Number.isNaN(due) || now.value < due) return 0

    // The token due at `nextRefillAt`, plus one for each whole interval since.
    return 1 + Math.floor((now.value - due) / REFILL_INTERVAL_MS)
  })

  const estimatedBalance = computed(() => {
    const s = status.value
    if (!s?.configured) return 0
    return Math.min(s.maxTokens, s.estimatedBalance + accrued.value)
  })

  const spendable = computed(() => {
    const s = status.value
    if (!s?.configured || s.keyDeleted) return 0
    return Math.max(0, estimatedBalance.value - s.reserveFloor)
  })

  /** Milliseconds until the next token, or null when there is nothing to wait for. */
  const msUntilNextRefill = computed(() => {
    const s = status.value
    if (!s?.configured || s.keyDeleted || !s.nextRefillAt) return null
    if (estimatedBalance.value >= s.maxTokens) return null

    const due = new Date(s.nextRefillAt).getTime()
    if (Number.isNaN(due)) return null

    const elapsedIntervals = Math.max(0, Math.ceil((now.value - due) / REFILL_INTERVAL_MS))
    return due + elapsedIntervals * REFILL_INTERVAL_MS - now.value
  })

  /**
   * Whether this deserves the operator's attention. Reaching zero deletes the key,
   * and only a person solving a CAPTCHA can replace it, so running low is the one
   * state worth interrupting for.
   */
  const needsAttention = computed(() => {
    const s = status.value
    if (!s?.configured) return false
    return s.keyDeleted || spendable.value <= 0
  })

  const start = () => {
    if (clock) return
    // A minute is enough: the smallest thing displayed is a countdown in minutes.
    clock = setInterval(() => {
      now.value = Date.now()
    }, 60_000)
  }

  const stop = () => {
    if (clock) {
      clearInterval(clock)
      clock = null
    }
  }

  return {
    status,
    isLoading,
    load,
    estimatedBalance,
    spendable,
    msUntilNextRefill,
    needsAttention,
    start,
    stop,
  }
})

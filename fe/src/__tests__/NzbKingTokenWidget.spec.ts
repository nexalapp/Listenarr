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
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'

const status = (overrides: Record<string, unknown> = {}) => ({
  configured: true,
  estimatedBalance: 100,
  maxTokens: 100,
  reserveFloor: 5,
  spendable: 95,
  nextRefillAt: null,
  lastSuccessfulUseAt: null,
  keyDeleted: false,
  summary: '',
  refusedRecently: 0,
  ...overrides,
})

const mountWidget = async (payload: Record<string, unknown>) => {
  vi.doMock('@/services/api', () => ({
    apiService: { getNzbKingStatus: vi.fn(async () => payload) },
  }))

  const { default: Widget } = await import('@/components/domain/nzbking/NzbKingTokenWidget.vue')
  const wrapper = mount(Widget)
  await flushPromises()
  return wrapper
}

describe('NzbKingTokenWidget', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
    setActivePinia(createPinia())
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('leads with what can be spent, not the raw balance', async () => {
    // Quoting both invites reconciling two numbers to reach the one that decides
    // whether a grab can happen. The reserve is never spendable.
    const wrapper = await mountWidget(status({ estimatedBalance: 100, spendable: 95 }))

    expect(wrapper.text()).toContain('≈95 available')
    expect(wrapper.text()).not.toContain('100 of 100')
  })

  it('says nothing about refills when it is already at the maximum', async () => {
    // The refill clause previously degraded to the word "full", which sat beside the
    // count reading as a third figure that disagreed with it.
    const wrapper = await mountWidget(status({ nextRefillAt: null }))

    expect(wrapper.text()).not.toContain('full')
    expect(wrapper.text()).not.toContain('+1 in')
  })

  it('counts down to the next token when one is actually due', async () => {
    const wrapper = await mountWidget(
      status({
        estimatedBalance: 40,
        spendable: 35,
        nextRefillAt: new Date(Date.now() + 23 * 60 * 1000).toISOString(),
      }),
    )

    expect(wrapper.text()).toMatch(/\+1 in \d+m/)
  })

  it('never reports how many were spent, which the count already reflects', async () => {
    const wrapper = await mountWidget(status({ refusedRecently: 0 }))

    expect(wrapper.text()).not.toContain('spent')
    expect(wrapper.text()).not.toContain('Last 24h')
  })

  it('reports refusals, because a grab that did not happen leaves no other trace', async () => {
    const wrapper = await mountWidget(status({ refusedRecently: 2 }))

    expect(wrapper.text()).toContain('2 grabs refused in the last 24h')
  })

  it('says one refusal in the singular', async () => {
    const wrapper = await mountWidget(status({ refusedRecently: 1 }))

    expect(wrapper.text()).toContain('1 grab refused in the last 24h')
    expect(wrapper.text()).not.toContain('1 grabs')
  })

  it('renders nothing at all when no key is configured', async () => {
    const wrapper = await mountWidget({
      configured: false,
      maxTokens: 100,
      reserveFloor: 5,
      estimatedBalance: 0,
      spendable: 0,
      keyDeleted: false,
      summary: '',
      refusedRecently: 0,
    })

    expect(wrapper.text().trim()).toBe('')
  })

  it('replaces the count with the remedy when the key is gone', async () => {
    const wrapper = await mountWidget(
      status({ keyDeleted: true, estimatedBalance: 0, spendable: 0 }),
    )

    expect(wrapper.text()).toContain('key deleted')
    expect(wrapper.text()).toContain('Request a new key')
    expect(wrapper.text()).not.toContain('available')
  })
})

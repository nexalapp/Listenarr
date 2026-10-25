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
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { AutoGrabSettings } from '@/types'

const api = vi.hoisted(() => ({
  getAutoGrabSettings: vi.fn(),
  saveAutoGrabSettings: vi.fn(),
  getAutoGrabPreview: vi.fn(),
  runAutoGrab: vi.fn(),
}))

vi.mock('@/services/api', () => ({ apiService: api }))
vi.mock('@/services/toastService', () => ({
  useToast: () => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }),
}))

import AutoGrabPanel from '@/components/domain/audiobook/AutoGrabPanel.vue'

const settings: AutoGrabSettings = {
  enabled: true,
  perRun: 10,
  intervalMinutes: 60,
  sources: [
    { kind: 'series', sectionId: null, enabled: true },
    { kind: 'discover', sectionId: '18580606011', enabled: true },
    { kind: 'authors', sectionId: null, enabled: false },
  ],
}

async function openPanel() {
  const wrapper = mount(AutoGrabPanel, {
    props: { labels: { authors: 'From your authors', series: 'From your series' } },
    global: { stubs: { LoadingState: true } },
  })
  await wrapper.find('.autograb-trigger').trigger('click')
  await flushPromises()
  return wrapper
}

describe('AutoGrabPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    api.getAutoGrabSettings.mockImplementation(async () => structuredClone(settings))
    api.saveAutoGrabSettings.mockImplementation(async (next: AutoGrabSettings) => next)
    api.getAutoGrabPreview.mockImplementation(async () => ({
      enabled: true,
      perRun: 10,
      available: 38,
      candidates: [
        {
          asin: 'B1',
          title: 'Drive',
          author: 'James S. A. Corey',
          indexerName: 'Abook.link',
          sourceKind: 'series',
          sourceLabel: 'From your series',
          book: { title: 'Drive', authors: ['James S. A. Corey'] },
        },
      ],
      bySource: [
        { kind: 'series', label: 'From your series', available: 12 },
        {
          kind: 'discover',
          sectionId: '18580606011',
          label: 'Science Fiction & Fantasy',
          available: 26,
        },
      ],
    }))
    api.runAutoGrab.mockImplementation(async () => ({
      startedAt: '2026-10-25T12:00:00Z',
      finishedAt: '2026-10-25T12:00:30Z',
      added: 9,
      failed: 1,
      outcomes: [],
    }))
  })

  it('lists the followed lists in the order a run draws from them', async () => {
    const wrapper = await openPanel()

    const labels = wrapper.findAll('.autograb-label').map((row) => row.text())
    expect(labels).toEqual(['From your series', 'Science Fiction & Fantasy', 'From your authors'])
  })

  it('names a chart category from the preview, which is where its name is known', async () => {
    const wrapper = await openPanel()

    // The page header cannot know what a followed section is called; the preview does.
    expect(wrapper.text()).toContain('Science Fiction & Fantasy')
  })

  it('says what a run would grab before anything is grabbed', async () => {
    const wrapper = await openPanel()

    expect(wrapper.text()).toContain('Would grab 1 of 38 waiting')
    expect(wrapper.text()).toContain('12 from From your series')
    expect(wrapper.text()).toContain('26 from Science Fiction & Fantasy')
    expect(api.runAutoGrab).not.toHaveBeenCalled()
  })

  it('moves a list up and saves the new order', async () => {
    const wrapper = await openPanel()

    // Second row's "up" arrow: each row has up then down.
    await wrapper.findAll('.autograb-move')[2]!.trigger('click')
    await flushPromises()

    const saved = api.saveAutoGrabSettings.mock.calls.at(-1)![0] as AutoGrabSettings
    expect(saved.sources.map((source) => source.kind)).toEqual(['discover', 'series', 'authors'])
  })

  it('unticks a list without disturbing the others', async () => {
    const wrapper = await openPanel()

    await wrapper.findAll('.autograb-list input[type="checkbox"]')[0]!.trigger('change')
    await flushPromises()

    const saved = api.saveAutoGrabSettings.mock.calls.at(-1)![0] as AutoGrabSettings
    expect(saved.sources[0]!.enabled).toBe(false)
    expect(saved.sources[1]!.enabled).toBe(true)
  })

  it('reports what a run did', async () => {
    const wrapper = await openPanel()

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('Run now'))!
      .trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('Added 9, 1 failed.')
  })

  it('turns the schedule off without forgetting the lists', async () => {
    const wrapper = await openPanel()

    // setValue, not trigger: the box is bound with :checked, so a bare change event
    // would report the state it already had.
    await wrapper.find('.autograb-switch input').setValue(false)
    await flushPromises()

    const saved = api.saveAutoGrabSettings.mock.calls.at(-1)![0] as AutoGrabSettings
    expect(saved.enabled).toBe(false)
    expect(saved.sources).toHaveLength(3)
  })
})

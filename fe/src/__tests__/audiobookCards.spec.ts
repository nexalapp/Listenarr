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
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import AudiobookCoverCard from '@/components/domain/audiobook/AudiobookCoverCard.vue'
import AudiobookListRow from '@/components/domain/audiobook/AudiobookListRow.vue'
import MonitoredBadge from '@/components/domain/audiobook/MonitoredBadge.vue'
import type { Audiobook } from '@/types'

/**
 * The one set of rules for what a person can do to a book from its cover, and
 * that both layouts follow them: the badge is the switch for a library book, the
 * magnifying glass is there for any book with no file, and a book not in the
 * library gets Add instead of Edit and Delete.
 */
const book = (overrides: Partial<Audiobook> = {}): Audiobook =>
  ({ id: 7, title: 'Wool', authors: ['Hugh Howey'], monitored: true, ...overrides }) as Audiobook

const layouts = [
  { name: 'grid', component: AudiobookCoverCard },
  { name: 'list', component: AudiobookListRow },
] as const

describe.each(layouts)('$name card', ({ component }) => {
  const mountCard = (props: Record<string, unknown>) =>
    mount(component, {
      props: { audiobook: book(), status: 'quality-match', statusLabel: 'Downloaded', ...props },
    })

  it('makes the monitored badge a switch for a library book, and a label otherwise', async () => {
    const inLibrary = mountCard({})
    const badge = inLibrary.findComponent(MonitoredBadge)
    expect(badge.find('button').exists()).toBe(true)
    expect(badge.text()).toContain('Monitored')
    await badge.find('button').trigger('click')
    expect(inLibrary.emitted('toggle-monitored')).toHaveLength(1)
    expect(inLibrary.emitted('open')).toBeUndefined()

    const notAdded = mountCard({ inLibrary: false })
    const label = notAdded.findComponent(MonitoredBadge)
    expect(label.find('button').exists()).toBe(false)
    expect(label.text()).toContain('Not Added')
  })

  it('offers a search only for a book with nothing on disk', () => {
    const search = (props: Record<string, unknown>) =>
      mountCard(props).find('[aria-label="Search for Wool"], .search-btn-small').exists()

    expect(search({ status: 'no-file' })).toBe(true)
    expect(search({ inLibrary: false, status: 'not-added' })).toBe(true)
    expect(search({ status: 'quality-match' })).toBe(false)
    expect(search({ status: 'downloading' })).toBe(false)
  })

  it('emits search without opening the book', async () => {
    const wrapper = mountCard({ status: 'no-file' })
    await wrapper.find('[aria-label="Search for Wool"], .search-btn-small').trigger('click')
    expect(wrapper.emitted('search')).toHaveLength(1)
    expect(wrapper.emitted('open')).toBeUndefined()
  })

  it('offers Add to a book not in the library, and Edit and Delete to one that is', async () => {
    const inLibrary = mountCard({})
    expect(inLibrary.find('.edit-btn-small').exists()).toBe(true)
    expect(inLibrary.find('.delete-btn-small').exists()).toBe(true)
    expect(inLibrary.find('.add-btn-small').exists()).toBe(false)

    const notAdded = mountCard({ inLibrary: false, status: 'not-added' })
    expect(notAdded.find('.add-btn-small').exists()).toBe(true)
    expect(notAdded.find('.edit-btn-small').exists()).toBe(false)
    await notAdded.find('.add-btn-small').trigger('click')
    expect(notAdded.emitted('add')).toHaveLength(1)
    expect(notAdded.emitted('open')).toBeUndefined()
  })

  it('offers no checkbox to a book that cannot be selected', () => {
    expect(mountCard({}).find('.selection-checkbox').exists()).toBe(true)
    expect(mountCard({ selectable: false }).find('.selection-checkbox').exists()).toBe(false)
  })

  it('opens the book on click and on Enter', async () => {
    const wrapper = mountCard({})
    const root = wrapper.find('.audiobook-item, .audiobook-list-item')
    await root.trigger('click')
    await root.trigger('keydown.enter')
    expect(wrapper.emitted('open')).toHaveLength(2)
  })
})

describe('MonitoredBadge', () => {
  it('reads as a switch: a toggle icon, pressed state, and a tooltip that says what a click does', () => {
    const on = mount(MonitoredBadge, { props: { monitored: true, inLibrary: true } })
    expect(on.find('button').attributes('aria-pressed')).toBe('true')
    expect(on.find('button').attributes('title')).toContain('click to stop monitoring')

    const off = mount(MonitoredBadge, { props: { monitored: false, inLibrary: true } })
    expect(off.find('button').attributes('aria-pressed')).toBe('false')
    expect(off.find('button').attributes('title')).toContain('click to monitor')
  })

  it('is disabled while a flip is in flight', () => {
    const busy = mount(MonitoredBadge, { props: { monitored: true, inLibrary: true, busy: true } })
    expect(busy.find('button').attributes('disabled')).toBeDefined()
  })
})

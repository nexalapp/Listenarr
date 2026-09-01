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
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import FieldLockToggle from '@/components/domain/audiobook/FieldLockToggle.vue'
import type { LockableField } from '@/types'

function mountToggle(modelValue: LockableField[]) {
  return mount(FieldLockToggle, {
    props: { field: 'title' as LockableField, name: 'Title', modelValue },
  })
}

describe('FieldLockToggle', () => {
  it('reads its state from the book’s lock set rather than its own', async () => {
    // One set for the whole form, so a save carries every padlock at once.
    expect(mountToggle(['description']).find('button').attributes('aria-pressed')).toBe('false')
    expect(mountToggle(['title']).find('button').attributes('aria-pressed')).toBe('true')
  })

  it('adds its field without disturbing the others', async () => {
    const wrapper = mountToggle(['description'])

    await wrapper.find('button').trigger('click')

    expect(wrapper.emitted('update:modelValue')![0][0]).toEqual(['description', 'title'])
  })

  it('removes only its own field', async () => {
    const wrapper = mountToggle(['title', 'description'])

    await wrapper.find('button').trigger('click')

    expect(wrapper.emitted('update:modelValue')![0][0]).toEqual(['description'])
  })

  it('says what the padlock will do, in both states', async () => {
    // The label is the only explanation of the feature an operator gets in passing, so it
    // says what a lock is for rather than just naming the state.
    expect(mountToggle([]).find('button').attributes('title')).toBe(
      'Pin Title so a metadata rescan cannot change it.',
    )
    expect(mountToggle(['title']).find('button').attributes('title')).toBe(
      'Title is pinned — a metadata rescan will not change it. Click to unpin.',
    )
  })
})

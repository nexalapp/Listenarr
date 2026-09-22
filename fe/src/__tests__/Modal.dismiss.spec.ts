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
import Modal from '@/components/feedback/Modal.vue'

// The dialog teleports to the body, so it is found there rather than in the wrapper.
const mountModal = (props: Record<string, unknown> = {}) =>
  mount(Modal, {
    props: { visible: true, title: 'A dialog', ...props },
    attachTo: document.body,
  })

const query = (selector: string) => {
  const element = document.body.querySelector(selector)
  if (!element) throw new Error(`No ${selector} in the document`)
  return element
}

const click = async (selector: string) => {
  query(selector).dispatchEvent(new MouseEvent('click', { bubbles: true }))
  await Promise.resolve()
}

const pressEscape = async () => {
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
  await Promise.resolve()
}

describe('Modal dismissal', () => {
  it('closes on a backdrop click and on Escape by default', async () => {
    const wrapper = mountModal()

    await click('.modal-overlay')
    expect(wrapper.emitted('close')).toHaveLength(1)

    await pressEscape()
    expect(wrapper.emitted('close')).toHaveLength(2)

    wrapper.unmount()
  })

  it('ignores a backdrop click and Escape when the dialog holds work', async () => {
    const wrapper = mountModal({ closeOnBackdrop: false, closeOnEscape: false })

    await click('.modal-overlay')
    await pressEscape()
    expect(wrapper.emitted('close')).toBeUndefined()

    // Its own button still closes it.
    await click('.close-btn')
    expect(wrapper.emitted('close')).toHaveLength(1)

    wrapper.unmount()
  })

  it('never closes on a click inside the dialog', async () => {
    const wrapper = mountModal()

    await click('.modal-content')

    expect(wrapper.emitted('close')).toBeUndefined()
    wrapper.unmount()
  })
})

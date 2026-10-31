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
import { defineComponent, h } from 'vue'
import AddFromAbookLink from '@/components/domain/search/AddFromAbookLink.vue'
import { apiService } from '@/services/api'

vi.mock('@/services/api', () => ({
  apiService: {
    getIndexers: vi.fn(),
    getAbookTopic: vi.fn(),
    sendAbookTopic: vi.fn(),
  },
}))

vi.mock('@/services/toastService', () => ({
  useToast: () => ({ success: vi.fn(), error: vi.fn() }),
}))

// The add form, reduced to what this component relies on: it receives the seeded book
// and reports the library record it created.
const AddLibraryModalStub = defineComponent({
  props: ['book', 'visible', 'autoSearchDefault'],
  emits: ['added', 'close'],
  setup(props, { emit }) {
    return () =>
      h('div', { class: 'add-modal' }, [
        h('span', { class: 'seed' }, JSON.stringify(props.book)),
        h('button', {
          class: 'confirm',
          onClick: () => emit('added', { id: 1273, title: 'Solaris' }),
        }),
      ])
  },
})

const api = apiService as unknown as {
  getIndexers: ReturnType<typeof vi.fn>
  getAbookTopic: ReturnType<typeof vi.fn>
  sendAbookTopic: ReturnType<typeof vi.fn>
}

async function mountConnected(connected = true) {
  api.getIndexers.mockResolvedValue(
    connected
      ? [{ implementation: 'AbookLink', isEnabled: true }]
      : [{ implementation: 'Newznab', isEnabled: true }],
  )
  const wrapper = mount(AddFromAbookLink, {
    global: { stubs: { AddLibraryModal: AddLibraryModalStub } },
  })
  await flushPromises()
  return wrapper
}

describe('AddFromAbookLink', () => {
  beforeEach(() => vi.clearAllMocks())

  it('is not offered when abook.link is not connected', async () => {
    const wrapper = await mountConnected(false)
    expect(wrapper.find('.abook-add').exists()).toBe(false)
  })

  it('adds the book from the post and downloads that post for it', async () => {
    api.getAbookTopic.mockResolvedValue({
      topicId: 71604,
      topicTitle: 'Stanislaw Lem - Solaris: The Definitive Edition (2011)',
      outcome: 'Complete',
      title: 'Solaris: The Definitive Edition',
      author: 'Stanislaw Lem, Bill Johnston',
      narrator: 'Alessandro Juliani',
      year: 2011,
    })
    api.sendAbookTopic.mockResolvedValue({ downloadId: 'd-1' })
    const wrapper = await mountConnected()

    await wrapper.find('.abook-toggle').trigger('click')
    await wrapper.find('.abook-input').setValue('https://abook.link/book/index.php?topic=71604')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(api.getAbookTopic).toHaveBeenCalledWith(71604)
    expect(wrapper.find('.abook-title').text()).toBe('Solaris: The Definitive Edition')

    await wrapper.find('.abook-preview .btn-primary').trigger('click')
    const seed = JSON.parse(wrapper.find('.seed').text())
    expect(seed).toMatchObject({
      asin: '',
      title: 'Solaris: The Definitive Edition',
      authors: ['Stanislaw Lem', 'Bill Johnston'],
      narrators: ['Alessandro Juliani'],
      publishYear: '2011',
      metadataSource: 'abook.link',
    })

    await wrapper.find('.confirm').trigger('click')
    await flushPromises()
    expect(api.sendAbookTopic).toHaveBeenCalledWith(71604, 1273)
  })

  it('accepts a bare topic number', async () => {
    api.getAbookTopic.mockResolvedValue({
      topicId: 51276,
      topicTitle: 'SpecOps',
      outcome: 'Complete',
    })
    const wrapper = await mountConnected()

    await wrapper.find('.abook-toggle').trigger('click')
    await wrapper.find('.abook-input').setValue(' 51276 ')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(api.getAbookTopic).toHaveBeenCalledWith(51276)
  })

  it('refuses a topic that is not a release, and anything that is not a post', async () => {
    api.getAbookTopic.mockResolvedValue({
      topicId: 9,
      topicTitle: 'Reading order',
      outcome: 'NotARelease',
    })
    const wrapper = await mountConnected()
    await wrapper.find('.abook-toggle').trigger('click')

    await wrapper.find('.abook-input').setValue('9')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.find('.abook-error').text()).toContain('not a release')
    expect(wrapper.find('.abook-preview').exists()).toBe(false)

    await wrapper.find('.abook-input').setValue('https://example.com/whatever')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.find('.abook-error').text()).toContain('not an abook.link post')
  })
})

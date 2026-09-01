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
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import type { TagPreview } from '@/types'

const previewTags = vi.fn()

vi.mock('@/services/api', () => ({
  apiService: {
    previewTags: (...args: unknown[]) => previewTags(...args),
  },
}))

const preview = (overrides: Partial<TagPreview> = {}): TagPreview => ({
  audiobookId: 42,
  title: 'Drive',
  canWrite: true,
  hasChanges: true,
  reason: null,
  files: [
    {
      fileId: 1,
      name: 'Drive.m4b',
      error: null,
      changes: [
        {
          tag: 'album',
          label: 'Album',
          current: 'Drive',
          proposed: '[The Expanse 2.7] Drive',
          action: 'Write',
          reason: 'Will be replaced.',
        },
        {
          tag: 'artist',
          label: 'Artist',
          current: 'James S. A. Corey',
          proposed: 'James S. A. Corey',
          action: 'Unchanged',
          reason: 'Already correct.',
        },
        {
          tag: 'description',
          label: 'Description',
          current: null,
          proposed: 'A short story of the Expanse.',
          action: 'Write',
          reason: 'Will be added.',
          isLongText: true,
        },
      ],
    },
  ],
  ...overrides,
})

async function mountPanel(result: TagPreview = preview(), props: Record<string, unknown> = {}) {
  previewTags.mockResolvedValue(result)
  const { default: AudiobookTagsPanel } =
    await import('@/components/domain/tagging/AudiobookTagsPanel.vue')

  const wrapper = mount(AudiobookTagsPanel, { props: { audiobookId: 42, ...props } })
  await new Promise((resolve) => setTimeout(resolve, 0))
  await wrapper.vm.$nextTick()
  return wrapper
}

describe('AudiobookTagsPanel', () => {
  beforeEach(() => {
    previewTags.mockReset()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows every tag, not only the ones that would change', async () => {
    // The tab is opened to check what the files say. A view that hid the correct values
    // could not be used for that.
    const wrapper = await mountPanel()

    expect(wrapper.text()).toContain('Album')
    expect(wrapper.text()).toContain('Artist')
    expect(wrapper.text()).toContain('Already correct.')
    expect(wrapper.findAll('tbody tr')).toHaveLength(3)
  })

  it('shows the value in the file beside an editable value to write', async () => {
    const wrapper = await mountPanel()

    expect(wrapper.find('td.col-current').text()).toBe('Drive')
    expect((wrapper.find('input.value-input').element as HTMLInputElement).value).toBe(
      '[The Expanse 2.7] Drive',
    )
  })

  it('gives a blurb a textarea rather than a single line', async () => {
    const wrapper = await mountPanel()

    const blurb = wrapper.find('textarea.value-input')
    expect(blurb.exists()).toBe(true)
    expect((blurb.element as HTMLTextAreaElement).value).toBe('A short story of the Expanse.')
  })

  it('selects nothing until the operator says so', async () => {
    // Writing replaces a library file. A screen arriving with every change ticked would
    // put that one careless click away.
    const wrapper = await mountPanel()

    const write = wrapper.findAll('.panel-actions button').at(-1)!
    expect(write.text()).toContain('Write 0 tags')
    expect(write.attributes('disabled')).toBeDefined()
  })

  it('emits the tags and the values that were on screen', async () => {
    const wrapper = await mountPanel()

    await wrapper.findAll('.panel-actions button')[0].trigger('click') // Select all changes
    await wrapper.findAll('.panel-actions button').at(-1)!.trigger('click')

    const payload = wrapper.emitted('write')![0][0] as {
      tags: string[]
      values: Record<string, string>
    }
    expect(payload.tags.sort()).toEqual(['album', 'description'])
    expect(payload.values.album).toBe('[The Expanse 2.7] Drive')
  })

  it('writes an edited value, and selects the tag it was typed into', async () => {
    const wrapper = await mountPanel()

    await wrapper.find('input.value-input').setValue('[The Expanse 0.1] Drive')
    expect(wrapper.text()).toContain('Will be written as edited.')

    await wrapper.findAll('.panel-actions button').at(-1)!.trigger('click')

    const payload = wrapper.emitted('write')![0][0] as {
      tags: string[]
      values: Record<string, string>
    }
    expect(payload.tags).toEqual(['album'])
    expect(payload.values.album).toBe('[The Expanse 0.1] Drive')
  })

  it('says why a book cannot be written to', async () => {
    const wrapper = await mountPanel(
      preview({
        canWrite: false,
        hasChanges: false,
        reason: 'This book has no M4B files to write tags into.',
        files: [],
      }),
    )

    expect(wrapper.text()).toContain('This book has no M4B files to write tags into.')
  })

  it('reports a file whose tags could not be read', async () => {
    const wrapper = await mountPanel(
      preview({
        files: [{ fileId: 1, name: 'Drive.m4b', error: 'the share went away', changes: [] }],
      }),
    )

    expect(wrapper.find('.tags-file-error').text()).toContain('the share went away')
  })
})

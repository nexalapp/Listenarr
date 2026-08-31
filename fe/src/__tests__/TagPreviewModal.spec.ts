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
          tag: 'description',
          label: 'Description',
          current: null,
          proposed: 'A short story of the Expanse.',
          action: 'Write',
          reason: 'Will be added.',
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
          tag: 'copyright',
          label: 'Copyright',
          current: '©2012',
          proposed: null,
          action: 'NotConfigured',
          reason: 'Left as it is: this tag is set never to be written.',
        },
      ],
    },
  ],
  ...overrides,
})

async function mountModal(result: TagPreview = preview()) {
  previewTags.mockResolvedValue(result)
  const { default: TagPreviewModal } =
    await import('@/components/domain/tagging/TagPreviewModal.vue')

  const wrapper = mount(TagPreviewModal, {
    props: { visible: true, audiobookId: 42 },
  })

  await new Promise((resolve) => setTimeout(resolve, 0))
  await wrapper.vm.$nextTick()
  return wrapper
}

describe('TagPreviewModal', () => {
  beforeEach(() => {
    previewTags.mockReset()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows the current and proposed value for each tag that will change', async () => {
    const wrapper = await mountModal()
    const text = wrapper.text()

    expect(text).toContain('Album')
    expect(text).toContain('Drive')
    expect(text).toContain('[The Expanse 2.7] Drive')
    expect(text).toContain('A short story of the Expanse.')
  })

  it('hides tags that will not change until asked for them', async () => {
    // Twenty tags of "already correct" would bury the two that matter.
    const wrapper = await mountModal()
    expect(wrapper.text()).not.toContain('Already correct.')

    await wrapper.find('.tag-show-all input').setValue(true)
    expect(wrapper.text()).toContain('Already correct.')
  })

  it('explains a tag it will not write rather than merely omitting it', async () => {
    const wrapper = await mountModal()
    await wrapper.find('.tag-show-all input').setValue(true)

    expect(wrapper.text()).toContain('Left as it is: this tag is set never to be written.')
  })

  it('starts with everything it would write already ticked', async () => {
    const wrapper = await mountModal()

    // The operator narrows a proposal; they do not assemble one from nothing.
    expect(wrapper.text()).toContain('2 of 2 tag(s) selected')
  })

  it('cannot tick a tag it would not write', async () => {
    const wrapper = await mountModal()
    await wrapper.find('.tag-show-all input').setValue(true)

    const boxes = wrapper.findAll('.tag-checkbox')
    const disabled = boxes.filter((box) => box.attributes('disabled') !== undefined)
    expect(disabled.length).toBe(2)
  })

  it('confirms with only the ticked tags', async () => {
    const wrapper = await mountModal()

    // Untick the album, leaving only the description.
    await wrapper.findAll('.tag-checkbox')[0].setValue(false)
    expect(wrapper.text()).toContain('1 of 2 tag(s) selected')

    await wrapper.find('.btn-primary').trigger('click')

    const emitted = wrapper.emitted('confirm')
    expect(emitted).toBeTruthy()
    expect(emitted![0][0]).toEqual(['description'])
  })

  it('cannot confirm with nothing ticked', async () => {
    const wrapper = await mountModal()

    await wrapper.find('.tag-toolbar-actions button:last-child').trigger('click')
    expect(wrapper.text()).toContain('0 of 2 tag(s) selected')

    const confirm = wrapper.find('.btn-primary')
    expect(confirm.attributes('disabled')).toBeDefined()

    await confirm.trigger('click')
    expect(wrapper.emitted('confirm')).toBeFalsy()
  })

  it('says so when the book is already correct', async () => {
    const wrapper = await mountModal(
      preview({
        hasChanges: false,
        files: [{ fileId: 1, name: 'Drive.m4b', error: null, changes: [] }],
      }),
    )

    expect(wrapper.text()).toContain('Every tag already matches')
    expect(wrapper.find('.btn-primary').attributes('disabled')).toBeDefined()
  })

  it('carries the reason through when tags cannot be written at all', async () => {
    const wrapper = await mountModal(
      preview({
        canWrite: false,
        hasChanges: false,
        reason: 'This book has no M4B files to write tags into.',
        files: [],
      }),
    )

    expect(wrapper.text()).toContain('This book has no M4B files to write tags into.')
  })

  it('reports a file whose current tags could not be read', async () => {
    const wrapper = await mountModal(
      preview({
        files: [
          {
            fileId: 1,
            name: 'Drive.m4b',
            error: 'This file is not readable from here.',
            changes: [],
          },
        ],
      }),
    )

    expect(wrapper.text()).toContain('This file is not readable from here.')
  })
})

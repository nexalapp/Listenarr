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
          isLongText: true,
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

  it('shows the current value, and the proposed one in an editable field', async () => {
    const wrapper = await mountModal()

    expect(wrapper.text()).toContain('Album')
    expect(wrapper.text()).toContain('Drive')

    const album = wrapper.find('input.tag-value-input')
    expect((album.element as HTMLInputElement).value).toBe('[The Expanse 2.7] Drive')
  })

  it('gives a long value a textarea rather than a single line', async () => {
    // A blurb typed into a one-line box cannot be read, let alone corrected.
    const wrapper = await mountModal()

    const blurb = wrapper.find('textarea.tag-value-input')
    expect(blurb.exists()).toBe(true)
    expect((blurb.element as HTMLTextAreaElement).value).toBe('A short story of the Expanse.')
  })

  it('sends an edited value instead of the one it proposed', async () => {
    // The point of the inputs: a provider gets a series position wrong often enough
    // that correcting one book should not mean editing the mapping every book shares.
    const wrapper = await mountModal()

    await wrapper.find('input.tag-value-input').setValue('[The Expanse 0.5] Drive')
    expect(wrapper.text()).toContain('Will be written as edited.')

    await wrapper.find('.btn-primary').trigger('click')

    const payload = wrapper.emitted('confirm')![0][0] as {
      tags: string[]
      values: Record<string, string>
    }
    expect(payload.values.album).toBe('[The Expanse 0.5] Drive')
  })

  it('sends the values it displayed even when nothing was edited', async () => {
    // The write is then the diff the operator approved, rather than whatever the
    // patterns happen to render by the time the worker gets to it.
    const wrapper = await mountModal()

    await wrapper.find('.btn-primary').trigger('click')

    const payload = wrapper.emitted('confirm')![0][0] as {
      tags: string[]
      values: Record<string, string>
    }
    expect(payload.values).toEqual({
      album: '[The Expanse 2.7] Drive',
      description: 'A short story of the Expanse.',
    })
  })

  it('can undo an edit and go back to the proposal', async () => {
    const wrapper = await mountModal()

    await wrapper.find('input.tag-value-input').setValue('Something else')
    expect(wrapper.find('.tag-edited-badge').exists()).toBe(true)

    await wrapper.find('.tag-value-revert').trigger('click')

    expect(wrapper.find('.tag-edited-badge').exists()).toBe(false)
    expect((wrapper.find('input.tag-value-input').element as HTMLInputElement).value).toBe(
      '[The Expanse 2.7] Drive',
    )
  })

  it('lets a typed value rescue a tag the preview would have skipped', async () => {
    // "No value for this book" is a statement about what Listenarr knows, not about
    // what the operator knows.
    const wrapper = await mountModal(
      preview({
        files: [
          {
            fileId: 1,
            name: 'Drive.m4b',
            error: null,
            changes: [
              {
                tag: 'SUBTITLE',
                label: 'Subtitle',
                current: null,
                proposed: null,
                action: 'NoValue',
                reason: 'Nothing to write: this book has no value for this tag.',
              },
            ],
          },
        ],
      }),
    )

    await wrapper.find('.tag-show-all input').setValue(true)
    expect(wrapper.text()).toContain('0 of 0 tag(s) selected')

    await wrapper.find('input.tag-value-input').setValue('An Expanse Short Story')

    expect(wrapper.text()).toContain('1 of 1 tag(s) selected')

    await wrapper.find('.btn-primary').trigger('click')
    const payload = wrapper.emitted('confirm')![0][0] as {
      tags: string[]
      values: Record<string, string>
    }
    expect(payload.tags).toEqual(['SUBTITLE'])
    expect(payload.values.SUBTITLE).toBe('An Expanse Short Story')
  })

  it('will not let a typed value reverse a never-write mapping', async () => {
    // That is a standing decision in Settings, and a preview is not the place to
    // undo it by accident.
    const wrapper = await mountModal()
    await wrapper.find('.tag-show-all input').setValue(true)

    const copyright = wrapper
      .findAll('.tag-change')
      .find((row) => row.text().includes('Copyright'))!
    const input = copyright.find('.tag-value-input')

    expect((input.element as HTMLInputElement).disabled).toBe(true)
    expect(copyright.find('.tag-checkbox').attributes('disabled')).toBeDefined()
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
    expect((emitted![0][0] as { tags: string[] }).tags).toEqual(['description'])
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

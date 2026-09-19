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
import type { ChapterRepairPreview } from '@/types'

const previewChapterRepair = vi.fn()

vi.mock('@/services/api', () => ({
  apiService: {
    previewChapterRepair: (...args: unknown[]) => previewChapterRepair(...args),
  },
}))

const preview = (overrides: Partial<ChapterRepairPreview> = {}): ChapterRepairPreview => ({
  audiobookId: 42,
  repairable: true,
  files: [
    {
      fileId: 1,
      name: 'War of Gifts.m4b',
      chapterHealth: 'corrupt',
      chapterReason: 'The chapter atom does not parse: version byte is 58',
      repairable: true,
      rejection: null,
      source: 'Played',
      partial: false,
      note: '25 chapter(s) from the file’s own chapter track.',
      chapters: Array.from({ length: 25 }, (_, i) => ({
        title: `Chapter ${i + 1}`,
        startSeconds: i * 300,
        endSeconds: (i + 1) * 300,
      })),
    },
    {
      fileId: 2,
      name: 'Fine.m4b',
      chapterHealth: 'healthy',
      chapterReason: '12 chapter(s).',
      repairable: false,
      rejection: "This file's chapter atom is not corrupt, so there is nothing to repair.",
      source: null,
      partial: false,
      note: null,
      chapters: null,
    },
  ],
  ...overrides,
})

async function mountModal(result: ChapterRepairPreview = preview()) {
  previewChapterRepair.mockResolvedValue(result)
  const { default: ChapterRepairModal } =
    await import('@/components/domain/tagging/ChapterRepairModal.vue')

  const wrapper = mount(ChapterRepairModal, {
    props: {
      visible: true,
      scopes: [{ audiobookId: 42, title: 'A War of Gifts', fileIds: [1, 2] }],
    },
  })

  await new Promise((resolve) => setTimeout(resolve, 0))
  await wrapper.vm.$nextTick()
  return wrapper
}

describe('ChapterRepairModal', () => {
  beforeEach(() => {
    previewChapterRepair.mockReset()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows the list a repair would write, and where it came from', async () => {
    const wrapper = await mountModal()

    expect(previewChapterRepair).toHaveBeenCalledWith(42, [1, 2])
    expect(wrapper.text()).toContain('25 chapters from the file’s chapter track')
    expect(wrapper.text()).toContain('Chapter 1')
    // Collapsed past a dozen rows; the rest come on request.
    expect(wrapper.findAll('.chapter-row')).toHaveLength(12)
    await wrapper.find('.chapter-more').trigger('click')
    expect(wrapper.findAll('.chapter-row')).toHaveLength(25)
  })

  it('lists a file it cannot repair with the reason, and leaves it out of the confirm', async () => {
    const wrapper = await mountModal()

    expect(wrapper.text()).toContain('not corrupt')
    await wrapper.find('.btn-primary').trigger('click')

    const confirmed = wrapper.emitted('confirm')
    expect(confirmed).toHaveLength(1)
    expect(confirmed![0][0]).toEqual([{ audiobookId: 42, fileIds: [1] }])
  })

  it('refuses when nothing is repairable', async () => {
    const wrapper = await mountModal(preview({ repairable: false, files: [preview().files[1]] }))

    expect(wrapper.text()).toContain('None of these files can be repaired')
    expect((wrapper.find('.btn-primary').element as HTMLButtonElement).disabled).toBe(true)
  })

  it('shows what was heard beside a chapter the narrator announced', async () => {
    const file = {
      ...preview().files[0],
      source: 'Announcements',
      chapters: [
        {
          title: 'Chapter 1: Saint Nick',
          startSeconds: 38,
          endSeconds: 1482,
          heard: '1. Saint Nick\nZach Morgan sat attentively.',
        },
        { title: 'Chapter 2', startSeconds: 1482, endSeconds: 2073, heard: null },
      ],
    }
    const wrapper = await mountModal(preview({ files: [file] }))

    expect(wrapper.text()).toContain('2 chapters from what the narrator announced')
    const heard = wrapper.findAll('.chapter-heard')
    expect(heard).toHaveLength(1)
    expect(heard[0].text()).toContain('1. Saint Nick · Zach Morgan sat attentively.')
  })

  it('waits, rather than refuses, while the fix is still being worked out', async () => {
    const file = {
      ...preview().files[0],
      repairable: false,
      planPending: true,
      source: null,
      chapters: null,
    }
    const wrapper = await mountModal(preview({ repairable: false, files: [file] }))

    expect(wrapper.text()).toContain('still being worked out')
    expect(wrapper.text()).not.toContain('None of these files can be repaired')
    const confirm = wrapper.findAll('button').find((b) => b.text().startsWith('Repair'))
    expect(confirm?.attributes('disabled')).toBeDefined()
  })

  it('leaves a pending file out of a repair of the others', async () => {
    const pending = {
      ...preview().files[1],
      chapterHealth: 'oversegmented' as const,
      rejection: null,
      planPending: true,
    }
    const wrapper = await mountModal(preview({ files: [preview().files[0], pending] }))

    expect(wrapper.text()).toContain('not part of this repair')
    expect(wrapper.text()).toContain('Repair 1 file(s)')
  })

  it('warns when the list is partial', async () => {
    const file = {
      ...preview().files[0],
      source: 'RecoveredAtom',
      partial: true,
      note: 'The opening chapters are lost.',
    }
    const wrapper = await mountModal(preview({ files: [file] }))

    expect(wrapper.text()).toContain('(partial)')
    expect(wrapper.text()).toContain('The opening chapters are lost.')
  })
})

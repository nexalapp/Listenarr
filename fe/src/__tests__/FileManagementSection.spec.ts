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
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'

describe('FileManagementSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('emits update:settings on pattern and select changes', async () => {
    const { default: FileManagementSection } =
      await import('@/components/settings/FileManagementSection.vue')
    const wrapper = mount(FileManagementSection, {
      props: {
        settings: {
          folderNamingPattern: '{Author}/{Series}/{Title}',
          fileNamingPattern: '{Title}',
          completedFileAction: 'move',
          importBlacklistExtensions: ['.nfo'],
        },
      },
    })

    const folderInput = wrapper.find('input[placeholder="{Author}/{Series}/{Title}"]')
    await folderInput.setValue('{Author}/{Title}')
    let last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.folderNamingPattern).toBe('{Author}/{Title}')

    const fileInput = wrapper.find('input[placeholder="{Title}"]')
    await fileInput.setValue('{Title}-{DiskNumber}')
    last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.fileNamingPattern).toBe('{Title}-{DiskNumber}')

    const sel = wrapper.find('select')
    await sel.setValue('hardlink/copy')
    last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.completedFileAction).toBe('hardlink/copy')

    const textarea = wrapper.find('textarea')
    await textarea.setValue('.nfo\njpg')
    await textarea.trigger('change')
    last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.importBlacklistExtensions).toEqual(['.nfo', '.jpg'])
  })

  it('shows preview for multi-file pattern with chapter numbers', async () => {
    const { default: FileManagementSection } =
      await import('@/components/settings/FileManagementSection.vue')
    const wrapper = mount(FileManagementSection, {
      props: {
        settings: {
          multiFileNamingPattern: '{Title}-Ch{ChapterNumber:00}',
        },
      },
    })

    // Preview should show simulated chapter numbers 01/02/03
    const preview = wrapper.find('.pattern-preview code')
    expect(preview.exists()).toBe(true)
    expect(preview.text()).toContain('Ch01')
    expect(preview.text()).toContain('Ch02')
    expect(preview.text()).toContain('Ch03')
  })

  it('shows path length warning when combined pattern exceeds 259 characters', async () => {
    const { default: FileManagementSection } =
      await import('@/components/settings/FileManagementSection.vue')
    // Use highly nested folder pattern that definitely exceeds 259 chars with sample values
    // Each {Author}/{Series}/{Title} expands to ~48 chars; six repetitions + output path + file will exceed 259
    const longPattern =
      '{Author}/{Series}/{Title}/{Author}/{Series}/{Title}/{Author}/{Series}/{Title}/{Author}/{Series}/{Title}/{Author}/{Series}/{Title}/{Author}/{Series}/{Title}'
    const wrapper = mount(FileManagementSection, {
      props: {
        settings: {
          outputPath: 'D:\\VeryLongAudiobookLibraryBasePath\\Collection',
          folderNamingPattern: longPattern,
          fileNamingPattern: '{Title}',
        },
      },
    })

    const warning = wrapper.find('.path-length-warning')
    expect(warning.exists()).toBe(true)
    expect(warning.text()).toContain('260 characters')
  })

  it('does not show path length warning when combined pattern is short', async () => {
    const { default: FileManagementSection } =
      await import('@/components/settings/FileManagementSection.vue')
    const wrapper = mount(FileManagementSection, {
      props: {
        settings: {
          outputPath: 'D:\\Books',
          folderNamingPattern: '{Author}/{Title}',
          fileNamingPattern: '{Title}',
        },
      },
    })

    const warning = wrapper.find('.path-length-warning')
    expect(warning.exists()).toBe(false)

    // Should show the "ok" indicator instead
    const ok = wrapper.find('.path-length-ok')
    expect(ok.exists()).toBe(true)
    expect(ok.text()).toContain('/ 259 characters')
  })
})

describe('FileManagementSection - conversion settings', () => {
  const mountWith = async (settings: Record<string, unknown>) => {
    const { default: FileManagementSection } =
      await import('@/components/settings/FileManagementSection.vue')
    return mount(FileManagementSection, { props: { settings } })
  }

  const lastEmitted = (wrapper: ReturnType<typeof mount>) => {
    const events = wrapper.emitted()['update:settings']!
    return events[events.length - 1][0] as Record<string, unknown>
  }

  it('shows the archive path when the disposition is Archive', async () => {
    // The API serialises the enum by name, so the option values must match its
    // casing exactly. Comparing against "archive" hid this input entirely.
    const wrapper = await mountWith({ conversionSourceDisposition: 'Archive' })

    expect(wrapper.find('input[placeholder="/mnt/user/audiobooks-archive"]').exists()).toBe(true)
  })

  it('shows the archive path by default, since Archive is the default', async () => {
    const wrapper = await mountWith({})

    expect(wrapper.find('input[placeholder="/mnt/user/audiobooks-archive"]').exists()).toBe(true)
  })

  it('hides the archive path for dispositions that never archive', async () => {
    for (const disposition of ['Keep', 'Delete']) {
      const wrapper = await mountWith({ conversionSourceDisposition: disposition })
      expect(wrapper.find('input[placeholder="/mnt/user/audiobooks-archive"]').exists()).toBe(false)
    }
  })

  it('offers exactly the values the API accepts', async () => {
    const wrapper = await mountWith({ conversionSourceDisposition: 'Archive' })
    const options = wrapper
      .findAll('option')
      .map((o) => o.attributes('value'))
      .filter((v) => v === 'Archive' || v === 'Keep' || v === 'Delete')

    expect(options).toEqual(['Archive', 'Keep', 'Delete'])
  })

  it('emits the archive path as typed', async () => {
    const wrapper = await mountWith({ conversionSourceDisposition: 'Archive' })
    const input = wrapper.find('input[placeholder="/mnt/user/audiobooks-archive"]')

    await input.setValue('/mnt/user/archive')
    await input.trigger('change')

    expect(lastEmitted(wrapper).conversionArchivePath).toBe('/mnt/user/archive')
  })

  it('emits the conversion toggle', async () => {
    const wrapper = await mountWith({ convertMp3ToM4b: false })
    const checkbox = wrapper.find('.conversion-toggle input[type="checkbox"]')

    await checkbox.setValue(true)

    expect(lastEmitted(wrapper).convertMp3ToM4b).toBe(true)
  })
})

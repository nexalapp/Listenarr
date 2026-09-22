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
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { vi, describe, it, expect, beforeEach } from 'vitest'

const apiMocks = vi.hoisted(() => ({
  getAudibleMetadata: vi.fn(),
  previewLibraryPath: vi.fn(),
  getApplicationSettings: vi.fn(),
  getQualityProfiles: vi.fn(),
  getRootFolders: vi.fn(),
  addToLibrary: vi.fn(),
}))

vi.mock('@/services/api', () => ({
  apiService: apiMocks,
}))

import AddLibraryModal from '@/components/domain/audiobook/AddLibraryModal.vue'
import type { AudibleBookMetadata } from '@/types'

const book = (rating?: AudibleBookMetadata['rating']): AudibleBookMetadata =>
  ({
    title: 'The Spy Who Haunted Me',
    authors: ['Simon R. Green'],
    narrators: ['James Langdon'],
    asin: 'B002V5ISK6',
    region: 'us',
    rating,
  }) as AudibleBookMetadata

const mountModal = async (rating?: AudibleBookMetadata['rating']) => {
  const wrapper = mount(AddLibraryModal, {
    props: { visible: true, book: book(rating) },
    attachTo: document.body,
    global: { plugins: [createPinia()] },
  })
  await flushPromises()
  return wrapper
}

describe('AddLibraryModal ratings', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    apiMocks.getAudibleMetadata.mockResolvedValue({})
    apiMocks.getApplicationSettings.mockResolvedValue({ outputPath: '/library' })
    apiMocks.getQualityProfiles.mockResolvedValue([])
    apiMocks.getRootFolders.mockResolvedValue([])
    apiMocks.previewLibraryPath.mockResolvedValue({ fullPath: '/library/book', relativePath: '' })
  })

  it('shows how the book was received, with the narration and story split', async () => {
    const wrapper = await mountModal({
      overall: { averageRating: 4.31294964028777, numRatings: 556 },
      performance: { averageRating: 4.417633410672853, numRatings: 431 },
      story: { averageRating: 4.320843091334894, numRatings: 427 },
      numReviews: 18,
    })

    const row = wrapper.get('.rating-row')
    expect(row.text()).toContain('4.3')
    expect(row.text()).toContain('556 ratings')
    expect(row.text()).toContain('18 written reviews')
    expect(row.text()).toContain('Narration 4.4')
    expect(row.text()).toContain('Story 4.3')

    wrapper.unmount()
  })

  it('leaves out a score the source did not give', async () => {
    const wrapper = await mountModal({ overall: { averageRating: 4, numRatings: 12 } })

    const row = wrapper.get('.rating-row')
    expect(row.text()).toContain('4.0')
    expect(row.text()).toContain('12 ratings')
    expect(row.text()).not.toContain('Narration')
    expect(row.text()).not.toContain('Story')
    expect(row.text()).not.toContain('written reviews')

    wrapper.unmount()
  })

  it('says nothing at all when the book has no ratings', async () => {
    const withoutRating = await mountModal(undefined)
    expect(withoutRating.find('.rating-row').exists()).toBe(false)
    withoutRating.unmount()

    // A source that answers with an empty distribution is the same as no answer.
    const empty = await mountModal({ overall: { averageRating: 0, numRatings: 0 } })
    expect(empty.find('.rating-row').exists()).toBe(false)
    empty.unmount()
  })
})

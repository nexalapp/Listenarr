/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
import { mount } from '@vue/test-utils'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import LibraryImportPreview, {
  activePreviewId,
} from '@/components/domain/audiobook/LibraryImportPreview.vue'

// jsdom ships no media pipeline: play/pause throw, and currentTime is read-only. Stand in
// a minimal one so the component's own limit logic is what the assertions exercise.
beforeAll(() => {
  Object.defineProperty(HTMLMediaElement.prototype, 'play', {
    configurable: true,
    value: vi.fn(function (this: HTMLMediaElement) {
      this.dispatchEvent(new Event('play'))
      return Promise.resolve()
    }),
  })
  Object.defineProperty(HTMLMediaElement.prototype, 'pause', {
    configurable: true,
    value: vi.fn(function (this: HTMLMediaElement) {
      this.dispatchEvent(new Event('pause'))
    }),
  })
  let currentTime = 0
  Object.defineProperty(HTMLMediaElement.prototype, 'currentTime', {
    configurable: true,
    get: () => currentTime,
    set: (value) => {
      currentTime = value
    },
  })
})

function mountPreview(overrides: Partial<{ itemId: string; path: string; rootFolderId: number }>) {
  return mount(LibraryImportPreview, {
    props: {
      itemId: 'row-1',
      path: '/books/Alpha/book.m4b',
      rootFolderId: 3,
      ...overrides,
    },
    attachTo: document.body,
  })
}

describe('LibraryImportPreview', () => {
  // Which row is playing is deliberately shared across every instance, so it outlives a
  // single mount and has to be cleared between cases.
  beforeEach(() => {
    activePreviewId.value = null
  })

  it('is inert until a root folder is chosen, because the URL is scoped to one', () => {
    const wrapper = mountPreview({ rootFolderId: null as unknown as number })

    expect(wrapper.get('.btn-preview').attributes('disabled')).toBeDefined()
    expect(wrapper.find('audio').exists()).toBe(false)
  })

  it('loads the file through the root-scoped preview endpoint when played', async () => {
    const wrapper = mountPreview({})

    await wrapper.get('.btn-preview').trigger('click')

    const src = wrapper.get('audio').attributes('src') ?? ''
    expect(src).toContain('/rootfolders/3/audio-preview')
    // The path is a query value, so it has to survive the spaces and slashes a book
    // folder carries.
    expect(src).toContain(`path=${encodeURIComponent('/books/Alpha/book.m4b')}`)
  })

  it('stops at the two-minute mark rather than playing on into the book', async () => {
    const wrapper = mountPreview({})
    await wrapper.get('.btn-preview').trigger('click')

    const audio = wrapper.get('audio').element as HTMLMediaElement
    audio.currentTime = 30
    await wrapper.get('audio').trigger('timeupdate')
    expect(wrapper.text()).toContain('0:30')

    audio.currentTime = 120
    await wrapper.get('audio').trigger('timeupdate')

    expect(audio.pause).toHaveBeenCalled()
    expect(audio.currentTime).toBe(0)
  })

  it('caps the seek bar at the preview window even for a long book', async () => {
    const wrapper = mountPreview({})
    await wrapper.get('.btn-preview').trigger('click')

    const audio = wrapper.get('audio').element as HTMLMediaElement
    Object.defineProperty(audio, 'duration', { configurable: true, value: 36_000 })
    await wrapper.get('audio').trigger('loadedmetadata')

    expect(wrapper.get('.preview-seek').attributes('max')).toBe('120')
    expect(wrapper.text()).toContain('2:00')
  })

  it('shortens the seek bar to a book that ends before the window does', async () => {
    const wrapper = mountPreview({})
    await wrapper.get('.btn-preview').trigger('click')

    const audio = wrapper.get('audio').element as HTMLMediaElement
    Object.defineProperty(audio, 'duration', { configurable: true, value: 45 })
    await wrapper.get('audio').trigger('loadedmetadata')

    expect(wrapper.get('.preview-seek').attributes('max')).toBe('45')
  })

  it('stops the playing row when another row starts, so two books never overlap', async () => {
    const first = mountPreview({ itemId: 'row-1' })
    const second = mountPreview({ itemId: 'row-2', path: '/books/Beta/book.mp3' })

    await first.get('.btn-preview').trigger('click')
    expect(first.find('audio').exists()).toBe(true)

    await second.get('.btn-preview').trigger('click')

    expect(second.find('audio').exists()).toBe(true)
    expect(first.find('audio').exists()).toBe(false)
  })

  it('tears the player down when the preview is closed', async () => {
    const wrapper = mountPreview({})
    await wrapper.get('.btn-preview').trigger('click')

    await wrapper.get('.preview-close').trigger('click')

    expect(wrapper.find('audio').exists()).toBe(false)
  })
})

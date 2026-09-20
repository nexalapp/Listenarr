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
import AudioPreviewPlayer from '@/components/ui/AudioPreviewPlayer.vue'
import { activePreviewId } from '@/composables/useAudioPreview'

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

function mountPreview(
  overrides: Partial<{
    previewId: string
    src: string
    label: string
    disabledTitle: string
    compact: boolean
  }>,
) {
  return mount(AudioPreviewPlayer, {
    props: {
      previewId: 'row-1',
      src: '/api/v1/rootfolders/3/audio-preview?path=%2Fbooks%2FAlpha%2Fbook.m4b',
      ...overrides,
    },
    attachTo: document.body,
  })
}

describe('AudioPreviewPlayer', () => {
  // Which row is playing is deliberately shared across every instance, so it outlives a
  // single mount and has to be cleared between cases.
  beforeEach(() => {
    activePreviewId.value = null
  })

  it('is inert with nothing to play, and says why', () => {
    const wrapper = mountPreview({ src: '', disabledTitle: 'Select a root folder first' })

    expect(wrapper.get('.btn-preview').attributes('disabled')).toBeDefined()
    expect(wrapper.get('.btn-preview').attributes('title')).toBe('Select a root folder first')
    expect(wrapper.find('audio').exists()).toBe(false)
  })

  it('loads the URL it was handed when played', async () => {
    const wrapper = mountPreview({})

    await wrapper.get('.btn-preview').trigger('click')

    expect(wrapper.get('audio').attributes('src')).toBe(
      '/api/v1/rootfolders/3/audio-preview?path=%2Fbooks%2FAlpha%2Fbook.m4b',
    )
  })

  it('shows only the button and the clock in compact mode, with no scrubber to push the row', async () => {
    const wrapper = mountPreview({ compact: true })

    await wrapper.get('.btn-preview').trigger('click')

    expect(wrapper.find('.preview-seek').exists()).toBe(false)
    expect(wrapper.get('.preview-time').text()).not.toContain('/')
    expect(wrapper.get('.preview').classes()).not.toContain('preview-open')
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
    const first = mountPreview({ previewId: 'row-1' })
    const second = mountPreview({ previewId: 'row-2', src: '/api/v1/tagging/files/9/audio' })

    await first.get('.btn-preview').trigger('click')
    expect(first.find('audio').exists()).toBe(true)

    await second.get('.btn-preview').trigger('click')

    expect(second.find('audio').exists()).toBe(true)
    expect(first.find('audio').exists()).toBe(false)
  })

  it('stops when the file underneath it changes, so the label never lies', async () => {
    const wrapper = mountPreview({})
    await wrapper.get('.btn-preview').trigger('click')
    expect(wrapper.find('audio').exists()).toBe(true)

    await wrapper.setProps({ src: '/api/v1/tagging/files/9/audio' })

    expect(wrapper.find('audio').exists()).toBe(false)
    expect(activePreviewId.value).toBeNull()
  })

  it('tears the player down when the preview is closed', async () => {
    const wrapper = mountPreview({})
    await wrapper.get('.btn-preview').trigger('click')

    await wrapper.get('.preview-close').trigger('click')

    expect(wrapper.find('audio').exists()).toBe(false)
  })
})

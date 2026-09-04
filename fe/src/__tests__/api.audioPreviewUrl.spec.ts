/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
import { describe, expect, it, vi } from 'vitest'
import { API_BASE_PATH } from '@/services/apiBase'

// Ensure we use the actual implementation (test-setup globally mocks /services/api)
vi.unmock('../services/api')
import { apiService } from '../services/api'

describe('ApiService.buildAudioPreviewUrl', () => {
  it('addresses the file through the root folder that owns it', () => {
    const url = apiService.buildAudioPreviewUrl(7, '/books/Alpha/book.m4b')

    expect(url.startsWith(`${API_BASE_PATH}/rootfolders/7/audio-preview`)).toBe(true)
  })

  it('escapes the path so a book folder cannot break the query string', () => {
    // Real folders carry spaces, ampersands and brackets from the naming pattern; left
    // raw, the & alone would truncate the path the server receives.
    const path = '/books/Smith, John/[The Expanse 2.7] Drive & Fall.m4b'

    const url = apiService.buildAudioPreviewUrl(1, path)

    expect(url).toBe(
      `${API_BASE_PATH}/rootfolders/1/audio-preview?path=${encodeURIComponent(path)}`,
    )
    expect(url).not.toContain(' ')
    expect(url.split('path=')[1]).not.toContain('&')
  })

  it('stays a same-origin path so the session cookie rides along', () => {
    // An <audio> element cannot set the X-Api-Key header, so the URL has to be one the
    // browser will attach the session cookie to.
    const url = apiService.buildAudioPreviewUrl(1, '/books/Alpha/book.mp3')

    expect(url.startsWith('/')).toBe(true)
    expect(url).not.toMatch(/^https?:/)
  })
})

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
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import LibraryImportRow from '@/components/domain/audiobook/LibraryImportRow.vue'
import { useLibraryImportStore, type LibraryImportItem } from '@/stores/libraryImport'

function buildItem(overrides: Partial<LibraryImportItem> = {}): LibraryImportItem {
  return {
    id: '/books/Alpha/book.m4b',
    fullPath: '/books/Alpha/book.m4b',
    sourceFiles: ['/books/Alpha/book.m4b'],
    folderPath: '/books/Alpha',
    relativePath: 'Alpha',
    folderName: 'Alpha',
    format: 'M4B',
    fileCount: 1,
    selectedMatch: null,
    fileMetadata: null,
    hasSearched: false,
    searchFailed: false,
    isSearching: false,
    selected: false,
    ...overrides,
  } as LibraryImportItem
}

function mountRow(item: LibraryImportItem, rootFolderId: number | null = 3) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const store = useLibraryImportStore()
  store.rootFolderId = rootFolderId
  store.items = { [item.id]: item }
  const wrapper = mount(LibraryImportRow, {
    props: { item },
    global: { plugins: [pinia] },
  })
  return { wrapper, store }
}

describe('LibraryImportRow file-metadata action', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('offers the file as a source before any search has run', async () => {
    // The whole point: reading the book's own tags used to require first running a
    // search and having it come back empty.
    const item = buildItem({ hasSearched: false })
    const { wrapper, store } = mountRow(item)
    const useFileMetadata = vi.spyOn(store, 'useFileMetadata').mockResolvedValue(null)

    const button = wrapper.get('.btn-use-file-toggle')
    expect(button.attributes('disabled')).toBeUndefined()

    await button.trigger('click')

    expect(useFileMetadata).toHaveBeenCalledWith(item.id)
  })

  it('still offers it once a search has come back empty', async () => {
    const item = buildItem({ hasSearched: true, searchFailed: true })
    const { wrapper, store } = mountRow(item)
    const useFileMetadata = vi.spyOn(store, 'useFileMetadata').mockResolvedValue(null)

    expect(wrapper.text()).toContain('No match found')
    await wrapper.get('.btn-use-file-toggle').trigger('click')

    expect(useFileMetadata).toHaveBeenCalledWith(item.id)
  })

  it('offers it as a replacement for a catalogue match the user disagrees with', async () => {
    const item = buildItem({
      hasSearched: true,
      selectedMatch: { title: 'Wrong Book', authors: [{ name: 'Someone Else' }] },
    } as Partial<LibraryImportItem>)
    const { wrapper, store } = mountRow(item)
    const useFileMetadata = vi.spyOn(store, 'useFileMetadata').mockResolvedValue(null)

    await wrapper.get('.btn-use-file-toggle').trigger('click')

    expect(useFileMetadata).toHaveBeenCalledWith(item.id)
  })

  it('reads as the current state once the file’s own tags are in use', () => {
    const item = buildItem({
      hasSearched: true,
      fileMetadata: { title: 'Alpha', authors: ['A. Author'] },
    } as Partial<LibraryImportItem>)
    const { wrapper } = mountRow(item)

    const button = wrapper.get('.btn-use-file-toggle')
    expect(button.classes()).toContain('active')
    expect(button.attributes('title')).toContain("Using the file's own")
  })

  it('is inert while a search is in flight, so it cannot race the result', () => {
    const item = buildItem({ isSearching: true })
    const { wrapper } = mountRow(item)

    expect(wrapper.get('.btn-use-file-toggle').attributes('disabled')).toBeDefined()
  })

  it('is inert with no root folder, which the metadata read is scoped to', () => {
    const item = buildItem()
    const { wrapper } = mountRow(item, null)

    expect(wrapper.get('.btn-use-file-toggle').attributes('disabled')).toBeDefined()
  })

  it('keeps the search action available alongside it', () => {
    const { wrapper } = mountRow(buildItem())

    expect(wrapper.find('.match-actions .btn-search-toggle').exists()).toBe(true)
    expect(wrapper.find('.match-actions .btn-use-file-toggle').exists()).toBe(true)
  })
})

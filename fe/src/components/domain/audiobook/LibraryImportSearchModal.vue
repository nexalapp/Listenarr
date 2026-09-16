<!--
  Listenarr - Audiobook Management System
  Copyright (C) 2024-2026 Listenarr Contributors

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU Affero General Public License as published
  by the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
  GNU Affero General Public License for more details.

  You should have received a copy of the GNU Affero General Public License
  along with this program. If not, see <https://www.gnu.org/licenses/>.
-->
<template>
  <Modal :visible="true" size="md" @close="emit('close')">
    <template #header>
      <ModalHeader :title="`Find Match — ${heading}`" @close="emit('close')" />
    </template>
    <ModalBody>
      <div class="search-wrap">
        <div class="search-fields">
          <div class="search-input-row">
            <input
              ref="inputEl"
              v-model="searchQuery"
              class="form-input search-input"
              placeholder="Title or ASIN…"
              @input="onInput"
              @keydown.escape="emit('close')"
              @keydown.enter="runSearch"
            />
          </div>
          <div class="search-input-row">
            <input
              v-model="authorQuery"
              class="form-input search-input"
              placeholder="Author (optional)…"
              @input="onInput"
              @keydown.escape="emit('close')"
              @keydown.enter="runSearch"
            />
            <PhSpinner v-if="isSearching" class="ph-spin search-spinner" :size="16" />
          </div>
          <label v-if="libraryLanguageLabel" class="any-language">
            <input v-model="anyLanguage" type="checkbox" @change="runSearch" />
            Any language <span class="muted">(otherwise {{ libraryLanguageLabel }})</span>
          </label>
        </div>

        <div v-if="searchResults.length > 0" class="results-list">
          <div
            v-for="result in searchResults"
            :key="result.asin ?? result.title"
            class="result-item"
            @click="select(result)"
          >
            <img
              v-if="result.imageUrl"
              :src="getProtectedImageSrc(result.imageUrl, placeholderUrl)"
              class="result-thumb"
              alt=""
            />
            <div class="result-info">
              <span class="result-title">{{ result.title }}</span>
              <span class="result-meta">
                {{ result.authors?.[0]?.name }}
                <span v-if="result.series">
                  ·
                  {{
                    Array.isArray(result.series) ? (result.series as any)[0]?.name : result.series
                  }}</span
                >
                <span v-if="result.asin" class="result-asin"> · {{ result.asin }}</span>
              </span>
            </div>
          </div>
        </div>

        <div v-else-if="hasSearched && !isSearching" class="no-results">
          No results for "{{ searchQuery }}"{{ authorQuery ? ` by "${authorQuery}"` : '' }}
        </div>

        <div v-else-if="!hasSearched && !isSearching" class="hint-text">
          Type a title or paste an ASIN to search
        </div>
      </div>
    </ModalBody>
  </Modal>
</template>

<script setup lang="ts">
import { computed, ref, onMounted, nextTick } from 'vue'
import { PhSpinner } from '@phosphor-icons/vue'
import { Modal, ModalHeader, ModalBody } from '@/components/feedback'
import { apiService } from '@/services/api'
import { useConfigurationStore } from '@/stores/configuration'
import { describeLanguageFilter, getLibraryLanguageFilter } from '@/utils/languageMapping'
import { useProtectedImages } from '@/composables/useProtectedImages'
import type { LibraryImportItem } from '@/stores/libraryImport'
import {
  buildLibraryImportInitialAuthor,
  buildLibraryImportInitialQuery,
} from '@/utils/libraryImportSearch'
import { getPlaceholderUrl } from '@/utils/placeholder'
import type { SearchResult } from '@/types'

// Either an import item, or a name plus the query to start from - the detail page
// uses the latter to re-match a book already in the library.
const props = defineProps<{
  item?: LibraryImportItem
  heading?: string
  initialQuery?: string
  initialAuthor?: string
}>()
const emit = defineEmits<{
  close: []
  select: [result: SearchResult]
}>()

const { getProtectedImageSrc } = useProtectedImages()
const inputEl = ref<HTMLInputElement | null>(null)
const placeholderUrl = getPlaceholderUrl()
// Build the initial query: ASIN → filename stem (when more specific than folder) → folderName
// detectedTitle comes from the audio file's "album" tag which is often the series name — skip it
function startingQuery(): string {
  if (props.item) return buildLibraryImportInitialQuery(props.item)
  return props.initialQuery ?? ''
}
const heading = computed(() => props.heading ?? props.item?.folderName ?? '')
const searchQuery = ref(startingQuery())
const authorQuery = ref(
  props.item ? buildLibraryImportInitialAuthor(props.item) : (props.initialAuthor ?? ''),
)
const configStore = useConfigurationStore()
const anyLanguage = ref(false)
const libraryLanguageLabel = computed(() => {
  const filter = getLibraryLanguageFilter(configStore.applicationSettings)
  return filter ? describeLanguageFilter(filter) : ''
})
const searchResults = ref<SearchResult[]>([])
const isSearching = ref(false)
const hasSearched = ref(false)

let debounceTimer: ReturnType<typeof setTimeout> | null = null

onMounted(async () => {
  await nextTick()
  inputEl.value?.focus()
  inputEl.value?.select()
  if (searchQuery.value.trim()) runSearch()
})

function onInput() {
  if (debounceTimer) clearTimeout(debounceTimer)
  debounceTimer = setTimeout(() => runSearch(), 400)
}

async function runSearch() {
  const q = searchQuery.value.trim()
  if (!q) return
  isSearching.value = true
  hasSearched.value = false
  try {
    const isAsin = /^[A-Z0-9]{10}$/i.test(q)
    // An ASIN names one edition; a title search stays within the library languages
    // unless widened, because the wrong-language edition is the usual mismatch.
    const language = anyLanguage.value
      ? undefined
      : getLibraryLanguageFilter(configStore.applicationSettings)
    const params = isAsin
      ? { asin: q, cap: 5 }
      : { title: q, author: authorQuery.value.trim() || undefined, cap: 5, language }
    searchResults.value = await apiService.advancedSearch(params)
    hasSearched.value = true
  } finally {
    isSearching.value = false
  }
}

function select(result: SearchResult) {
  emit('select', result)
  emit('close')
}
</script>

<style scoped>
.any-language {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.8rem;
  color: #bbb;
}

.any-language .muted {
  color: #777;
}

.search-wrap {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.search-fields {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.search-input-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.search-input {
  flex: 1;
}

.search-spinner {
  color: #888;
  flex-shrink: 0;
}

.results-list {
  max-height: 320px;
  overflow-y: auto;
  border: 1px solid #333;
  border-radius: 6px;
}

.result-item {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.5rem 0.75rem;
  cursor: pointer;
  transition: background 0.15s;
  border-bottom: 1px solid #2a2a2a;
}

.result-item:last-child {
  border-bottom: none;
}

.result-item:hover {
  background: #2a2a2a;
}

.result-thumb {
  width: 36px;
  height: 36px;
  object-fit: cover;
  border-radius: 3px;
  flex-shrink: 0;
}

.result-info {
  min-width: 0;
  flex: 1;
}

.result-title {
  display: block;
  font-size: 0.875rem;
  color: #e0e0e0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.result-meta {
  display: block;
  font-size: 0.75rem;
  color: #888;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.result-asin {
  font-family: monospace;
  font-size: 0.7rem;
}

.no-results,
.hint-text {
  padding: 0.75rem 0;
  font-size: 0.85rem;
  color: #666;
  text-align: center;
}
</style>

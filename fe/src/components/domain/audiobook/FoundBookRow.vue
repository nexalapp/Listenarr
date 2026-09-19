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
  <tr class="found-row" :class="{ busy: match.busy }">
    <td class="cell-book" data-label="Book">
      <span class="book-title" :title="item.title ?? undefined">{{ displayTitle }}</span>
      <span v-if="metaLine" class="book-meta">{{ metaLine }}</span>
      <span class="book-folder" :title="item.bookFolder">{{ folderLabel }}</span>
    </td>

    <td class="cell-files" data-label="Files">
      <span class="format-badge" v-if="item.format">{{ item.format }}</span>
      <span class="file-count"
        >{{ item.audioFileCount }} file{{ item.audioFileCount === 1 ? '' : 's' }}</span
      >
      <span class="file-meta">{{ sizeLabel }} · {{ durationLabel }}</span>
      <details v-if="item.files.length > 1" class="file-list">
        <summary>Show files</summary>
        <ul>
          <li v-for="file in item.files" :key="file.path" :title="file.path">
            <span :class="{ companion: !file.isAudio, broken: !!file.error }">{{
              fileName(file.path)
            }}</span>
            <span v-if="file.error" class="file-error">{{ file.error }}</span>
          </li>
        </ul>
      </details>
    </td>

    <td class="cell-complete" data-label="Complete">
      <Pill
        :variant="completenessVariant"
        size="small"
        :title="item.completenessReason ?? undefined"
      >
        {{ item.completeness }}
      </Pill>
      <span class="reason">{{ item.completenessReason }}</span>
    </td>

    <td class="cell-library" data-label="Library">
      <Pill :variant="libraryVariant" size="small">{{ libraryLabel }}</Pill>
      <RouterLink
        v-if="item.matchedAudiobookId"
        :to="`/books/${item.matchedAudiobookId}`"
        class="library-link"
      >
        Open
      </RouterLink>
    </td>

    <td class="cell-match" data-label="Match">
      <template v-if="item.state === 'Pending' || item.state === 'Importing'">
        <div v-if="match.isSearching" class="match-status">
          <PhSpinner class="ph-spin" :size="14" /> Searching…
        </div>
        <div v-else-if="match.selectedMatch" class="match-status matched">
          <PhCheckCircle :size="14" class="ok" />
          <div class="match-copy">
            <span
              class="match-title"
              :title="match.selectedMatch.asin ? `ASIN: ${match.selectedMatch.asin}` : undefined"
            >
              {{ match.selectedMatch.title }}
            </span>
            <span v-if="match.selectedMatch.authors?.length" class="match-author">
              {{ match.selectedMatch.authors[0]?.name }}
            </span>
          </div>
        </div>
        <div v-else-if="match.searchFailed" class="match-status warn">
          <PhWarningCircle :size="14" /> Lookup failed
        </div>
        <div v-else-if="match.hasSearched" class="match-status warn">
          <PhWarningCircle :size="14" /> No match
        </div>
        <div v-else class="match-status">—</div>

        <label
          v-if="match.selectedMatch"
          class="separate-book"
          title="Add as its own record even when the library seems to hold this edition."
        >
          <input
            type="checkbox"
            :checked="match.separateBook"
            @change="store.setSeparateBook(item.id, ($event.target as HTMLInputElement).checked)"
          />
          <span>Separate book</span>
        </label>
      </template>
      <span
        v-else-if="item.state === 'Blocked'"
        class="blocked-reason"
        :title="item.blockedReason ?? undefined"
      >
        {{ item.blockedReason }}
      </span>
      <span v-else class="state-label">
        {{ item.state }}
        <Pill v-if="item.autoAdded" variant="info" size="small" title="Added by the automatic add"
          >auto</Pill
        >
      </span>
    </td>

    <td class="cell-actions" data-label="Actions">
      <div class="actions">
        <template v-if="item.state === 'Pending'">
          <button
            class="btn btn-icon"
            title="Search for a match"
            :disabled="match.busy"
            @click="showSearch = true"
          >
            <PhMagnifyingGlass :size="14" />
          </button>
          <button
            class="btn btn-primary btn-sm"
            :disabled="!canAdd"
            :title="addTitle"
            @click="emit('add', item.id)"
          >
            <PhSpinner v-if="match.busy" class="ph-spin" :size="14" />
            <PhPlus v-else :size="14" />
            Add
          </button>
          <button
            class="btn btn-secondary btn-sm"
            :disabled="match.busy"
            @click="store.decide(item.id, 'ignore')"
          >
            Ignore
          </button>
          <button
            class="btn btn-danger btn-sm"
            :disabled="match.busy"
            @click="emit('discard', item.id)"
          >
            <PhTrash :size="14" /> Discard
          </button>
        </template>
        <template v-else-if="item.state === 'Blocked'">
          <button
            v-if="item.blockedKind !== 'OwnedByDownload'"
            class="btn btn-secondary btn-sm"
            :disabled="match.busy"
            @click="store.decide(item.id, 'ignore')"
          >
            Ignore
          </button>
          <button
            v-if="item.blockedKind === 'Settling'"
            class="btn btn-danger btn-sm"
            :disabled="match.busy"
            @click="emit('discard', item.id)"
          >
            <PhTrash :size="14" /> Discard
          </button>
        </template>
        <template v-else-if="item.state === 'Ignored'">
          <button
            class="btn btn-secondary btn-sm"
            :disabled="match.busy"
            @click="store.decide(item.id, 'restore')"
          >
            Restore
          </button>
          <button
            class="btn btn-danger btn-sm"
            :disabled="match.busy"
            @click="emit('discard', item.id)"
          >
            <PhTrash :size="14" /> Discard
          </button>
        </template>
        <span v-else-if="item.state === 'Importing'" class="state-label">
          <PhSpinner class="ph-spin" :size="14" /> Importing…
        </span>
      </div>
      <span v-if="match.error" class="row-error" :title="match.error">{{ match.error }}</span>
    </td>
  </tr>

  <LibraryImportSearchModal
    v-if="showSearch"
    heading="Match found book"
    :initial-query="item.asin ?? item.title ?? folderLabel"
    :initial-author="item.asin ? '' : (item.author ?? '')"
    @close="showSearch = false"
    @select="onSelect"
  />
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink } from 'vue-router'
import {
  PhCheckCircle,
  PhMagnifyingGlass,
  PhPlus,
  PhSpinner,
  PhTrash,
  PhWarningCircle,
} from '@phosphor-icons/vue'
import { Pill } from '@/components/base'
import LibraryImportSearchModal from '@/components/domain/audiobook/LibraryImportSearchModal.vue'
import { folderName, useFoundBooksStore } from '@/stores/foundBooks'
import type { FoundBook, SearchResult } from '@/types'

const props = defineProps<{ item: FoundBook; canImport: boolean }>()
const emit = defineEmits<{ add: [id: number]; discard: [id: number] }>()

const store = useFoundBooksStore()
const showSearch = ref(false)

const match = computed(() => store.matchState(props.item.id))
const folderLabel = computed(() => folderName(props.item.bookFolder))
const displayTitle = computed(() => props.item.title?.trim() || folderLabel.value)
const metaLine = computed(() =>
  [
    props.item.author,
    props.item.series
      ? `${props.item.series}${props.item.seriesPosition ? ` ${props.item.seriesPosition}` : ''}`
      : null,
    props.item.year,
  ]
    .filter(Boolean)
    .join(' · '),
)

const sizeLabel = computed(() => {
  const gb = props.item.totalBytes / 1024 ** 3
  return gb >= 1 ? `${gb.toFixed(1)} GB` : `${(props.item.totalBytes / 1024 ** 2).toFixed(0)} MB`
})
const durationLabel = computed(() => {
  const total = Math.round(props.item.totalDurationSeconds)
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  return h > 0 ? `${h}h ${m}m` : `${m}m`
})

const completenessVariant = computed(() => {
  switch (props.item.completeness) {
    case 'Complete':
      return 'success'
    case 'Incomplete':
      return 'warning'
    case 'Corrupt':
      return 'error'
    default:
      return 'subtle'
  }
})

const libraryLabel = computed(() => {
  switch (props.item.libraryStatus) {
    case 'InLibrary':
      return 'In library'
    case 'Wanted':
      return 'Wanted'
    default:
      return 'New'
  }
})
const libraryVariant = computed(() => {
  switch (props.item.libraryStatus) {
    case 'InLibrary':
      return 'info'
    case 'Wanted':
      return 'primary'
    default:
      return 'subtle'
  }
})

const canAdd = computed(
  () => props.canImport && !match.value.busy && match.value.selectedMatch != null,
)
const addTitle = computed(() => {
  if (!props.canImport) return 'Choose a library folder first'
  if (!match.value.selectedMatch) return 'Match the book first'
  if (props.item.libraryStatus === 'InLibrary')
    return 'The library already holds this; tick "Separate book" to add another copy'
  return 'Add to the library and move the files'
})

function fileName(path: string): string {
  return folderName(path)
}

function onSelect(result: SearchResult) {
  store.selectMatch(props.item.id, result)
  showSearch.value = false
}
</script>

<style scoped>
.found-row td {
  vertical-align: top;
  padding: 0.65rem 0.6rem;
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
}

.found-row.busy {
  opacity: 0.7;
}

.cell-book {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 14rem;
}

.book-title {
  font-weight: 600;
}

.book-meta,
.book-folder,
.file-meta,
.reason,
.blocked-reason,
.state-label {
  color: #999;
  font-size: 0.8rem;
}

.book-folder {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 22rem;
}

.cell-files {
  min-width: 9rem;
}

.format-badge {
  display: inline-block;
  padding: 0.1rem 0.4rem;
  border-radius: 4px;
  background: rgba(255, 255, 255, 0.08);
  font-size: 0.75rem;
  margin-right: 0.4rem;
}

.file-count {
  font-size: 0.85rem;
}

.file-meta {
  display: block;
}

.file-list summary {
  cursor: pointer;
  font-size: 0.8rem;
  color: #999;
}

.file-list ul {
  list-style: none;
  padding: 0.25rem 0 0;
  margin: 0;
  font-size: 0.78rem;
  max-height: 12rem;
  overflow: auto;
}

.file-list .companion {
  color: #777;
}

.file-list .broken {
  color: var(--error-400, #e57373);
}

.file-error {
  color: var(--error-400, #e57373);
  margin-left: 0.4rem;
}

.cell-complete .reason {
  display: block;
  margin-top: 0.25rem;
  max-width: 18rem;
}

.library-link {
  display: block;
  font-size: 0.8rem;
  margin-top: 0.25rem;
}

.match-status {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.85rem;
}

.match-status .ok {
  color: var(--success-400, #81c784);
}

.match-status.warn {
  color: var(--warning-400, #ffb74d);
}

.match-copy {
  display: flex;
  flex-direction: column;
}

.match-author {
  color: #999;
  font-size: 0.8rem;
}

.separate-book {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  font-size: 0.78rem;
  color: #999;
  margin-top: 0.3rem;
}

.actions {
  display: flex;
  gap: 0.35rem;
  flex-wrap: wrap;
}

.btn-icon {
  padding: 0.3rem 0.45rem;
}

.row-error {
  display: block;
  margin-top: 0.3rem;
  color: var(--error-400, #e57373);
  font-size: 0.78rem;
  max-width: 20rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>

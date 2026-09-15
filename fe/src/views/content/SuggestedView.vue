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
  <div class="suggested-view">
    <div class="page-header">
      <h1>
        <PhSparkle />
        Suggested
      </h1>
      <div class="header-actions">
        <Checkbox v-model="searchOnAdd" class="search-on-add">Search for downloads on add</Checkbox>
        <span v-if="refreshStatus?.running" class="refresh-progress">
          Fetching catalogs… {{ refreshStatus.completed }} / {{ refreshStatus.total }}
          <template v-if="refreshStatus.current"> · {{ refreshStatus.current }}</template>
        </span>
        <button
          class="btn btn-secondary"
          :disabled="refreshStatus?.running || refreshing"
          title="Fetch catalogs for library authors and series that have none yet. This is the only thing on this page that goes to Audible."
          @click="startRefresh"
        >
          <PhArrowsClockwise :class="{ spinning: refreshStatus?.running }" />
          Refresh
        </button>
      </div>
    </div>

    <p v-if="snapshot" class="coverage">
      Based on cached catalogs for
      <strong>{{ snapshot.coverage.authorsWithCatalog }}</strong> of
      {{ snapshot.coverage.authorsInLibrary }} authors and
      <strong>{{ snapshot.coverage.seriesWithCatalog }}</strong> of
      {{ snapshot.coverage.seriesInLibrary }} series in your library.
      <template v-if="uncovered > 0"> Refresh to look at the other {{ uncovered }}. </template>
    </p>

    <div class="tabs">
      <button
        v-for="tab in tabs"
        :key="tab.id"
        class="tab"
        :class="{ active: activeTab === tab.id }"
        @click="activeTab = tab.id"
      >
        {{ tab.label }}
        <Pill v-if="tab.count > 0" variant="count" size="small">{{ tab.count }}</Pill>
      </button>
    </div>

    <LoadingState v-if="loading" message="Working out what you're missing..." />

    <EmptyState v-else-if="error" title="Could not load suggestions" :message="error" />

    <template v-else-if="snapshot">
      <!-- Missing from your authors -->
      <template v-if="activeTab === 'authors'">
        <EmptyState
          v-if="snapshot.authors.length === 0"
          title="Nothing missing from your authors"
          :message="
            snapshot.coverage.authorsWithCatalog === 0
              ? 'No author catalogs are cached yet. Refresh to fetch them.'
              : 'Every cached author catalog is fully represented in your library.'
          "
        />
        <section v-for="group in snapshot.authors" :key="group.author" class="group">
          <header class="group-header">
            <RouterLink
              :to="{ name: 'collection', params: { type: 'author', name: group.author } }"
              class="group-title"
            >
              {{ group.author }}
            </RouterLink>
            <span class="group-meta">
              {{ group.libraryCount }} in library · {{ group.missing.length }} missing
            </span>
          </header>
          <div class="cards">
            <SuggestedBookCard
              v-for="book in group.missing"
              :key="bookKey(book)"
              :book="book"
              @add="openAdd(book)"
            />
          </div>
        </section>
      </template>

      <!-- Missing from your series -->
      <template v-else-if="activeTab === 'series'">
        <EmptyState
          v-if="snapshot.series.length === 0"
          title="No gaps in your series"
          :message="
            snapshot.coverage.seriesWithCatalog === 0
              ? 'No series catalogs are cached yet. Refresh to fetch them.'
              : 'Every cached series is complete in your library.'
          "
        />
        <section v-for="group in snapshot.series" :key="group.series" class="group">
          <header class="group-header">
            <RouterLink
              :to="{ name: 'collection', params: { type: 'series', name: group.series } }"
              class="group-title"
            >
              {{ group.series }}
            </RouterLink>
            <span class="group-meta">
              {{ group.libraryCount }} in library · {{ group.missing.length }} missing
            </span>
          </header>
          <div class="cards">
            <SuggestedBookCard
              v-for="book in group.missing"
              :key="bookKey(book)"
              :book="book"
              show-position
              @add="openAdd(book)"
            />
          </div>
        </section>
      </template>

      <!-- Authors like yours -->
      <template v-else>
        <EmptyState
          v-if="snapshot.relatedAuthors.length === 0"
          title="No related authors yet"
          message="Audible's 'similar authors' come with each author catalog. Refresh to fetch them."
        />
        <div v-else class="related-list">
          <RouterLink
            v-for="author in snapshot.relatedAuthors"
            :key="author.name"
            :to="{ name: 'collection', params: { type: 'author', name: author.name } }"
            class="related-row"
          >
            <span class="related-name">{{ author.name }}</span>
            <span class="related-because"
              >because you have {{ formatBecause(author.because) }}</span
            >
          </RouterLink>
        </div>
      </template>
    </template>

    <AddLibraryModal
      v-if="pendingAddBook"
      :visible="true"
      :book="pendingAddBook"
      :auto-search-default="searchOnAdd"
      @close="pendingAddBook = null"
      @added="handleAdded"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { PhArrowsClockwise, PhSparkle } from '@phosphor-icons/vue'
import { EmptyState, LoadingState, Pill } from '@/components/base'
import { Checkbox } from '@/components/form'
import AddLibraryModal from '@/components/domain/audiobook/AddLibraryModal.vue'
import SuggestedBookCard from '@/components/domain/audiobook/SuggestedBookCard.vue'
import { apiService } from '@/services/api'
import { useToast } from '@/services/toastService'
import { buildCatalogMetadata } from '@/utils/catalogMetadata'
import { describeApiError } from '@/utils/apiError'
import type {
  AudibleBookMetadata,
  SuggestedBook,
  SuggestionRefreshStatus,
  SuggestionSnapshot,
} from '@/types'

type TabId = 'authors' | 'series' | 'related'

const toast = useToast()

const snapshot = ref<SuggestionSnapshot | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const activeTab = ref<TabId>('authors')
const pendingAddBook = ref<AudibleBookMetadata | null>(null)

// Whether adding from here also kicks off a release search. On by default - a
// suggestion someone acts on is one they want now - and remembered per browser.
const SEARCH_ON_ADD_KEY = 'listenarr.suggested.searchOnAdd'
const searchOnAdd = ref(readSearchOnAdd())
watch(searchOnAdd, (value) => {
  try {
    localStorage.setItem(SEARCH_ON_ADD_KEY, value ? '1' : '0')
  } catch {
    // Storage unavailable: the toggle still works for this visit.
  }
})

function readSearchOnAdd(): boolean {
  try {
    return localStorage.getItem(SEARCH_ON_ADD_KEY) !== '0'
  } catch {
    return true
  }
}

const refreshStatus = ref<SuggestionRefreshStatus | null>(null)
const refreshing = ref(false)
let refreshPoll: number | null = null

const tabs = computed(() => [
  {
    id: 'authors' as TabId,
    label: 'From your authors',
    count: snapshot.value?.authors.reduce((n, g) => n + g.missing.length, 0) ?? 0,
  },
  {
    id: 'series' as TabId,
    label: 'From your series',
    count: snapshot.value?.series.reduce((n, g) => n + g.missing.length, 0) ?? 0,
  },
  {
    id: 'related' as TabId,
    label: 'Authors like yours',
    count: snapshot.value?.relatedAuthors.length ?? 0,
  },
])

const uncovered = computed(() => {
  const c = snapshot.value?.coverage
  if (!c) return 0
  return c.authorsInLibrary - c.authorsWithCatalog + (c.seriesInLibrary - c.seriesWithCatalog)
})

function bookKey(book: SuggestedBook): string {
  return book.asin || `${book.title}|${book.authors[0] ?? ''}`
}

function formatBecause(names: string[]): string {
  if (names.length <= 2) return names.join(' and ')
  return `${names.slice(0, 2).join(', ')} and ${names.length - 2} more`
}

async function load() {
  loading.value = true
  error.value = null
  try {
    snapshot.value = await apiService.getSuggestions()
  } catch (e) {
    error.value = describeApiError(e, 'Suggestions could not be loaded.')
  } finally {
    loading.value = false
  }
}

async function pollRefresh() {
  try {
    const status = await apiService.getSuggestionRefreshStatus()
    const wasRunning = refreshStatus.value?.running ?? false
    refreshStatus.value = status
    if (status.running) {
      if (refreshPoll == null) {
        refreshPoll = window.setInterval(pollRefresh, 3000)
      }
      return
    }
    stopPolling()
    if (wasRunning) {
      // A refresh just finished: the cache has more in it than the page shows.
      await load()
    }
  } catch {
    stopPolling()
  }
}

function stopPolling() {
  if (refreshPoll != null) {
    window.clearInterval(refreshPoll)
    refreshPoll = null
  }
}

async function startRefresh() {
  refreshing.value = true
  try {
    refreshStatus.value = await apiService.refreshSuggestions()
    if (refreshStatus.value.total === 0) {
      toast.info(
        'Nothing to fetch',
        'Every author and series in your library already has a catalog.',
      )
    }
    await pollRefresh()
  } catch (e) {
    toast.error('Refresh not started', describeApiError(e, 'A refresh may already be running.'))
    await pollRefresh()
  } finally {
    refreshing.value = false
  }
}

function openAdd(book: SuggestedBook) {
  pendingAddBook.value = buildCatalogMetadata(book)
}

async function handleAdded() {
  pendingAddBook.value = null
  await load()
}

onMounted(async () => {
  await Promise.all([load(), pollRefresh()])
})

onBeforeUnmount(stopPolling)
</script>

<style scoped>
.suggested-view {
  padding: 1em;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
  gap: 1rem;
  flex-wrap: wrap;
}

.page-header h1 {
  margin: 0;
  color: white;
  font-size: 2rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.page-header h1 svg {
  color: #fcc419;
  width: 32px;
  height: 32px;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.search-on-add {
  color: #bbb;
  font-size: 0.85rem;
}

.refresh-progress {
  color: #aaa;
  font-size: 0.85rem;
}

.spinning {
  animation: spin 1.2s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.coverage {
  color: #999;
  margin: 0 0 1.25rem;
  font-size: 0.9rem;
}

.coverage strong {
  color: #ddd;
}

.tabs {
  display: flex;
  gap: 0.25rem;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  margin-bottom: 1.5rem;
  overflow-x: auto;
}

.tab {
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  color: #aaa;
  padding: 0.6rem 0.9rem;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  white-space: nowrap;
  font-size: 0.95rem;
}

.tab:hover {
  color: white;
}

.tab.active {
  color: white;
  border-bottom-color: var(--brand-500);
}

.group {
  margin-bottom: 2rem;
}

.group-header {
  display: flex;
  align-items: baseline;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
}

.group-title {
  color: white;
  font-size: 1.15rem;
  font-weight: 600;
  text-decoration: none;
}

.group-title:hover {
  text-decoration: underline;
}

.group-meta {
  color: #888;
  font-size: 0.85rem;
}

.cards {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
  gap: 1rem;
}

.related-list {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.related-row {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.6rem 0.9rem;
  border-radius: 6px;
  text-decoration: none;
  color: inherit;
  flex-wrap: wrap;
}

.related-row:hover {
  background: rgba(255, 255, 255, 0.05);
}

.related-name {
  color: white;
  font-weight: 500;
}

.related-because {
  color: #888;
  font-size: 0.85rem;
}
</style>

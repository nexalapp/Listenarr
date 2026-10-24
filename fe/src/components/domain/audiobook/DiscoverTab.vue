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
  <div class="discover">
    <div class="discover-bar">
      <p class="discover-blurb">{{ blurb }}</p>
      <div class="discover-actions">
        <span v-if="status?.running" class="discover-progress">
          {{ statusLabel }}
        </span>
        <DiscoverSectionPicker
          :source="source"
          :label="pickerLabel"
          :noun="noun"
          :source-name="sourceName"
          :depth-help="depthHelp"
          :max-pages="maxPages"
          @saved="onSelectionSaved"
        />
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="busy || !feed?.available"
          :title="`Re-read the newest page of each ${nounSingular}`"
          @click="refresh"
        >
          <PhArrowsClockwise :class="{ spinning: status?.running }" />
          Refresh
        </button>
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="busy || !canLoadMore"
          :title="loadMoreTitle"
          @click="loadMore"
        >
          Load more
        </button>
      </div>
    </div>

    <LoadingState v-if="loading" :message="`Reading what ${sourceName} offered…`" />

    <EmptyState v-else-if="error" title="Could not load this tab" :message="error" />

    <EmptyState
      v-else-if="feed && !feed.available"
      :title="`${sourceName} is not available`"
      :message="feed.unavailable ?? ''"
    />

    <EmptyState
      v-else-if="feed && feed.sections.length === 0"
      :title="`No ${noun} chosen yet`"
      :message="`Pick the ${noun} you want in this tab with the ${pickerLabel} button.`"
    />

    <template v-else-if="feed">
      <section v-for="section in feed.sections" :key="section.sectionId" class="group">
        <header class="group-header">
          <span class="group-title">{{ section.sectionName }}</span>
          <span class="group-meta">
            {{ section.books.length }} to look at
            <template v-if="section.held > 0"> · {{ section.held }} already yours</template>
            <template v-if="section.pagesFetched > 0">
              · {{ section.pagesFetched }} {{ section.pagesFetched === 1 ? 'page' : 'pages' }} read
            </template>
            <template v-if="section.lastFetchedAt">
              · {{ timeAgo(section.lastFetchedAt) }}
            </template>
          </span>
          <span v-if="section.lastError" class="group-error" :title="section.lastError">
            Last fetch failed
          </span>
        </header>

        <p v-if="section.books.length === 0 && section.pagesFetched === 0" class="group-empty">
          Nothing read yet. Refresh, or wait for the background fetch.
        </p>
        <p v-else-if="section.books.length === 0" class="group-empty">
          Everything here is already in your library, or ignored.
        </p>

        <div v-else class="cards">
          <SuggestedBookCard
            v-for="entry in section.books"
            :key="`${section.sectionId}:${entry.book.asin || entry.book.title}:${entry.rank}`"
            :book="entry.book"
            :rank="source === 'charts' ? entry.rank : undefined"
            :note="noteFor(entry)"
            :note-url="entry.sourceUrl"
            :added="addedKeys.has(bookKey(entry.book))"
            :adding="addingKeys.has(bookKey(entry.book))"
            @add="emit('add', entry.book)"
            @search="emit('search', entry.book)"
            @open="emit('open', entry.book)"
            @ignore="ignore(entry.book)"
          />
        </div>
      </section>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { PhArrowsClockwise } from '@phosphor-icons/vue'
import { EmptyState, LoadingState } from '@/components/base'
import SuggestedBookCard from '@/components/domain/audiobook/SuggestedBookCard.vue'
import DiscoverSectionPicker from '@/components/domain/audiobook/DiscoverSectionPicker.vue'
import { apiService } from '@/services/api'
import { useToast } from '@/services/toastService'
import { describeApiError } from '@/utils/apiError'
import type {
  DiscoverEntry,
  DiscoverFeed,
  DiscoverRefreshStatus,
  DiscoverSource,
  SuggestedBook,
} from '@/types'

const props = defineProps<{
  source: DiscoverSource
  /** Keys of books added during this visit, so a card can show "Added". */
  addedKeys: Set<string>
  addingKeys: Set<string>
}>()

const emit = defineEmits<{
  add: [book: SuggestedBook]
  search: [book: SuggestedBook]
  open: [book: SuggestedBook]
}>()

const toast = useToast()

const feed = ref<DiscoverFeed | null>(null)
const status = ref<DiscoverRefreshStatus | null>(null)
const loading = ref(true)
const busy = ref(false)
const error = ref<string | null>(null)
let poll: number | null = null

const charts = computed(() => props.source === 'charts')
const sourceName = computed(() => (charts.value ? 'Audible' : 'abook.link'))
const pickerLabel = computed(() => (charts.value ? 'Categories' : 'Boards'))
const noun = computed(() => (charts.value ? 'categories' : 'boards'))
const nounSingular = computed(() => (charts.value ? 'category' : 'board'))
const maxPages = computed(() => (charts.value ? 5 : 50))
const blurb = computed(() =>
  charts.value
    ? 'What is selling on Audible right now, in the categories you follow. Books you already have are counted, not listed.'
    : 'What has just been posted to the boards you follow. Requests and reading orders are left out.',
)
const depthHelp = computed(() =>
  charts.value
    ? "Twenty books a page. Audible's chart ends at 100, so five pages is all of it."
    : 'Twenty topics a page, loaded a page at a time in the background.',
)

const statusLabel = computed(() => {
  const value = status.value
  if (!value?.running) return ''
  const where = value.current ? ` · ${value.current}` : ''
  return `Fetching ${value.completed}/${value.total}${where}`
})

const canLoadMore = computed(() => {
  if (!feed.value?.available) return false
  return feed.value.sections.some((section) => section.pagesFetched < feed.value!.pagesPerSection)
})

const loadMoreTitle = computed(() =>
  canLoadMore.value
    ? `Read one more page of each ${nounSingular.value} now`
    : `Every ${nounSingular.value} is loaded as deep as the setting allows`,
)

function bookKey(book: SuggestedBook): string {
  return book.asin || `${book.title}|${book.authors[0] ?? ''}`
}

/** The line under a card: what the source said, and when it said it. */
function noteFor(entry: DiscoverEntry): string | undefined {
  const parts: string[] = []
  if (entry.postedAt) parts.push(timeAgo(entry.postedAt))
  if (!entry.resolved) parts.push('not looked up yet')
  if (props.source !== 'charts' && entry.sourceNote) parts.push(entry.sourceNote)
  return parts.length > 0 ? parts.join(' · ') : undefined
}

function timeAgo(value: string): string {
  const then = new Date(value).getTime()
  if (Number.isNaN(then)) return ''
  const minutes = Math.max(0, Math.round((Date.now() - then) / 60000))
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.round(hours / 24)
  return days < 30 ? `${days}d ago` : new Date(value).toLocaleDateString()
}

/**
 * "Not interested", answered here rather than upstairs: this tab owns its list, and
 * the card has to leave it at once. The decision is the same one the other tabs
 * record, so an ignored book stays ignored wherever it would otherwise turn up.
 */
async function ignore(book: SuggestedBook) {
  try {
    await apiService.ignoreSuggestion({
      asin: book.asin,
      title: book.title,
      authors: book.authors,
    })
    if (!feed.value) return
    const key = bookKey(book)
    feed.value = {
      ...feed.value,
      sections: feed.value.sections.map((section) => ({
        ...section,
        books: section.books.filter((entry) => bookKey(entry.book) !== key),
      })),
    }
  } catch (e) {
    toast.error('Could not ignore', describeApiError(e, 'The book is still listed.'))
  }
}

async function load() {
  loading.value = feed.value === null
  error.value = null
  try {
    feed.value = await apiService.getDiscover(props.source)
    status.value = feed.value.status
    if (status.value?.running) startPolling()
  } catch (e) {
    error.value = describeApiError(e, 'This tab could not be loaded.')
  } finally {
    loading.value = false
  }
}

function startPolling() {
  poll ??= window.setInterval(pollStatus, 3000)
}

function stopPolling() {
  if (poll != null) {
    window.clearInterval(poll)
    poll = null
  }
}

async function pollStatus() {
  try {
    const next = await apiService.getDiscoverRefreshStatus(props.source)
    const wasRunning = status.value?.running ?? false
    status.value = next
    if (!next.running) {
      stopPolling()
      // A fetch just finished: the cache holds more than the page shows.
      if (wasRunning) await load()
    }
  } catch {
    stopPolling()
  }
}

async function run(action: () => Promise<DiscoverRefreshStatus>, whenBusy: string) {
  busy.value = true
  try {
    status.value = await action()
    startPolling()
  } catch (e) {
    toast.info('Nothing started', describeApiError(e, whenBusy))
    await pollStatus()
  } finally {
    busy.value = false
  }
}

const refresh = () =>
  run(() => apiService.refreshDiscover(props.source), 'A fetch is already running.')

const loadMore = () =>
  run(
    () => apiService.loadMoreDiscover(props.source),
    'A fetch is already running, or everything is already loaded.',
  )

async function onSelectionSaved() {
  await load()
  // A section nobody has read yet shows nothing until something fetches it.
  await run(() => apiService.refreshDiscover(props.source), 'A fetch is already running.')
}

onMounted(load)
onBeforeUnmount(stopPolling)
</script>

<style scoped>
.discover {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.discover-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

.discover-blurb {
  margin: 0;
  color: #aaa;
  font-size: 0.85rem;
  max-width: 48ch;
}

.discover-actions {
  display: flex;
  align-items: center;
  gap: 0.6rem;
}

.discover-progress {
  color: #aaa;
  font-size: 0.8rem;
}

.group {
  margin-bottom: 1.5rem;
}

.group-header {
  display: flex;
  align-items: baseline;
  gap: 0.75rem;
  flex-wrap: wrap;
  margin-bottom: 0.6rem;
  border-bottom: 1px solid #2f2f2f;
  padding-bottom: 0.35rem;
}

.group-title {
  color: #fff;
  font-size: 1.05rem;
  font-weight: 600;
}

.group-meta {
  color: #888;
  font-size: 0.8rem;
}

.group-error {
  color: #e5a0a0;
  font-size: 0.78rem;
}

.group-empty {
  color: #888;
  font-size: 0.85rem;
  margin: 0.3rem 0 0 0;
}

/* Two across, the same as the catalogue tabs: the card puts its facts beside the
   cover, and a narrower column stacks the title one word per line. */
.cards {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.9rem;
}

@media (max-width: 900px) {
  .cards {
    grid-template-columns: minmax(0, 1fr);
  }
}

.spinning {
  animation: spin 1.2s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>

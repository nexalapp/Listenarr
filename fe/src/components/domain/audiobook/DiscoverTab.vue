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

    <div v-if="feed && feed.sections.length > 0" class="discover-view">
      <label class="view-control">
        <span>Sort</span>
        <select v-model="sort" class="view-select" :aria-label="`Sort ${noun}`">
          <option v-for="option in sortOptions" :key="option.id" :value="option.id">
            {{ option.label }}
          </option>
        </select>
      </label>
      <label
        class="view-control"
        :title="`Off, the ${noun} become one list and a book in two of them is listed once.`"
      >
        <input v-model="grouped" type="checkbox" />
        <span>Group by {{ nounSingular }}</span>
      </label>
      <span v-if="!grouped && mergedCount > 0" class="view-note">
        {{ mergedCount }} {{ mergedCount === 1 ? 'book was' : 'books were' }} in more than one
        {{ nounSingular }}
      </span>
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
      <section v-for="section in displaySections" :key="section.key" class="group">
        <header class="group-header">
          <span class="group-title">{{ section.title }}</span>
          <span class="group-meta">
            {{ section.entries.length }} to look at
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
          <label v-if="grouped && section.key !== 'all'" class="group-autograb">
            <input
              type="checkbox"
              :checked="autoGrab.includes(source === 'charts' ? 'discover' : 'abook', section.key)"
              @change="autoGrab.toggle(source === 'charts' ? 'discover' : 'abook', section.key)"
            />
            Auto-grab
          </label>
        </header>

        <p v-if="section.entries.length === 0 && section.pagesFetched === 0" class="group-empty">
          Nothing read yet. Refresh, or wait for the background fetch.
        </p>
        <p v-else-if="section.entries.length === 0" class="group-empty">
          Everything here is already in your library, or ignored.
        </p>

        <div v-else class="cards">
          <SuggestedBookCard
            v-for="entry in section.entries"
            :key="`${section.key}:${bookKey(entry.book)}`"
            :book="entry.book"
            :rank="grouped && source === 'charts' ? entry.rank : undefined"
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
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { PhArrowsClockwise } from '@phosphor-icons/vue'
import { EmptyState, LoadingState } from '@/components/base'
import SuggestedBookCard from '@/components/domain/audiobook/SuggestedBookCard.vue'
import DiscoverSectionPicker from '@/components/domain/audiobook/DiscoverSectionPicker.vue'
import { apiService } from '@/services/api'
import { useAutoGrabStore } from '@/stores/autoGrab'
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
const autoGrab = useAutoGrabStore()

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

/** An entry as the page shows it: the feed's, plus every section it turned up in. */
type DisplayEntry = DiscoverEntry & { sections: string[] }

type DisplaySection = {
  key: string
  title: string
  held: number
  pagesFetched: number
  lastFetchedAt?: string
  lastError?: string
  entries: DisplayEntry[]
}

type SortId = 'default' | 'rating' | 'ratings' | 'newest' | 'title'

const sortOptions = computed<{ id: SortId; label: string }[]>(() => [
  { id: 'default', label: charts.value ? 'Chart position' : 'Recently posted' },
  { id: 'rating', label: 'Highest rated' },
  { id: 'ratings', label: 'Most ratings' },
  { id: 'newest', label: 'Newest' },
  { id: 'title', label: 'Title' },
])

// How the list is laid out is a per-person preference, not a setting the server
// needs: it is remembered per browser, per tab.
const sort = ref<SortId>(readStored<SortId>('sort', 'default', isSortId))
const grouped = ref(
  readStored<boolean>('grouped', true, (v): v is boolean => typeof v === 'boolean'),
)

watch([sort, grouped], () => {
  writeStored('sort', sort.value)
  writeStored('grouped', grouped.value)
})

function isSortId(value: unknown): value is SortId {
  return (
    value === 'default' ||
    value === 'rating' ||
    value === 'ratings' ||
    value === 'newest' ||
    value === 'title'
  )
}

function storageKey(name: string): string {
  return `listenarr.discover.${props.source}.${name}`
}

function readStored<T>(name: string, fallback: T, isValid: (value: unknown) => value is T): T {
  try {
    const raw = localStorage.getItem(storageKey(name))
    if (raw === null) return fallback
    const parsed: unknown = JSON.parse(raw)
    return isValid(parsed) ? parsed : fallback
  } catch {
    // Storage unavailable or holding nonsense: the default still works.
    return fallback
  }
}

function writeStored(name: string, value: unknown) {
  try {
    localStorage.setItem(storageKey(name), JSON.stringify(value))
  } catch {
    // The choice still holds for this visit.
  }
}

/**
 * What the page renders: one block per section, or one block for the lot.
 *
 * Ungrouped is not just the sections concatenated. A book that sells well is in
 * several chart categories at once, and listing it three times is three chances
 * to act on the same book; so the duplicates are merged, the copy with the best
 * standing is kept, and the card says which sections it came from.
 */
const displaySections = computed<DisplaySection[]>(() => {
  const sections = feed.value?.sections ?? []
  if (grouped.value) {
    return sections.map((section) => ({
      key: section.sectionId,
      title: section.sectionName,
      held: section.held,
      pagesFetched: section.pagesFetched,
      lastFetchedAt: section.lastFetchedAt,
      lastError: section.lastError,
      entries: sorted(
        section.books.map((entry) => ({ ...entry, sections: [section.sectionName] })),
      ),
    }))
  }

  return [
    {
      key: 'all',
      title: `All ${noun.value}`,
      held: sections.reduce((total, section) => total + section.held, 0),
      pagesFetched: sections.reduce((total, section) => total + section.pagesFetched, 0),
      lastFetchedAt: sections
        .map((section) => section.lastFetchedAt)
        .filter((at): at is string => !!at)
        .sort()
        .at(-1),
      lastError: sections.find((section) => section.lastError)?.lastError,
      entries: sorted(merged.value),
    },
  ]
})

/** Every section's books as one list, each book once. */
const merged = computed<DisplayEntry[]>(() => {
  const byBook = new Map<string, DisplayEntry>()

  for (const section of feed.value?.sections ?? []) {
    for (const entry of section.books) {
      const key = bookKey(entry.book)
      const existing = byBook.get(key)
      if (!existing) {
        byBook.set(key, { ...entry, sections: [section.sectionName] })
        continue
      }

      // Keep the copy that stands best: the higher chart place, or the newer
      // post. Its own section leads the list, because that is the one its rank
      // and its link belong to.
      if (beats(entry, existing)) {
        byBook.set(key, {
          ...entry,
          sections: [section.sectionName, ...existing.sections],
        })
      } else {
        existing.sections.push(section.sectionName)
      }
    }
  }

  return [...byBook.values()]
})

const mergedCount = computed(() => merged.value.filter((entry) => entry.sections.length > 1).length)

function beats(candidate: DiscoverEntry, incumbent: DiscoverEntry): boolean {
  if (charts.value) return candidate.rank < incumbent.rank
  return (candidate.postedAt ?? '') > (incumbent.postedAt ?? '')
}

function sorted(entries: DisplayEntry[]): DisplayEntry[] {
  const list = [...entries]
  switch (sort.value) {
    case 'rating':
      return list.sort((a, b) => weightedRating(b.book) - weightedRating(a.book))
    case 'ratings':
      return list.sort((a, b) => (b.book.ratingCount ?? 0) - (a.book.ratingCount ?? 0))
    case 'newest':
      return list.sort((a, b) => released(b) - released(a))
    case 'title':
      return list.sort((a, b) => a.book.title.localeCompare(b.book.title))
    default:
      // The source's own order. Within a section the feed already arrives that
      // way, but across sections it does not: two boards concatenated would list
      // yesterday's posts from the first above this morning's from the second.
      return list.sort((a, b) =>
        charts.value
          ? a.rank - b.rank
          : (b.postedAt ?? '').localeCompare(a.postedAt ?? '') || a.rank - b.rank,
      )
  }
}

/**
 * A rating worth sorting by.
 *
 * Audible's average alone puts a book with three five-star ratings above one with
 * seventy thousand at 4.8, which is not what "highest rated" means to anyone. Each
 * average is pulled toward the middle by how little it is based on, so a rating has
 * to be earned by enough listeners to beat a well-rated favourite. A book nobody has
 * rated sorts last rather than in the middle.
 */
function weightedRating(book: SuggestedBook): number {
  const rating = book.ratingOverall
  if (rating == null) return -1
  const count = book.ratingCount ?? 0
  const prior = 4.2
  const weight = 50
  return (count * rating + weight * prior) / (count + weight)
}

function released(entry: DiscoverEntry): number {
  const when = entry.postedAt ?? entry.book.publishedDate
  const parsed = when ? Date.parse(when) : Number.NaN
  return Number.isNaN(parsed) ? 0 : parsed
}

/** The line under a card: what the source said, and when it said it. */
function noteFor(entry: DisplayEntry): string | undefined {
  const parts: string[] = []
  if (entry.postedAt) parts.push(timeAgo(entry.postedAt))
  if (!entry.resolved) parts.push('not looked up yet')
  // Ungrouped, the card is the only place left that can say where a book came
  // from. A chart place belongs to one chart, so it moves here with the name of
  // that chart: two books both badged "#1" from different categories read as a
  // bug, while "#1 in Science Fiction & Fantasy" reads as the fact it is.
  if (!grouped.value && entry.sections.length > 0) {
    const [home, ...also] = entry.sections
    parts.push(charts.value ? `#${entry.rank} in ${home}` : home!)
    if (also.length > 0) parts.push(`also in ${also.join(', ')}`)
  }
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

onMounted(async () => {
  void autoGrab.load()
  await load()
})
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

.discover-view {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
  margin-top: -0.4rem;
}

.view-control {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  color: #bbb;
  font-size: 0.82rem;
  cursor: pointer;
}

.view-select {
  background: #1b1b1b;
  border: 1px solid #3a3a3a;
  border-radius: 4px;
  color: #ddd;
  padding: 0.2rem 0.4rem;
  font-size: 0.82rem;
}

.view-note {
  color: #777;
  font-size: 0.78rem;
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

.group-autograb {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  color: #999;
  font-size: 0.78rem;
  cursor: pointer;
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

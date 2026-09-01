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
  <div class="tags-view">
    <div class="toolbar">
      <div class="toolbar-left">
        <span v-if="!loading" class="count-badge">
          {{ visibleRows.length }} of {{ rows.length }} file{{ rows.length === 1 ? '' : 's' }}
        </span>
        <span v-if="mismatchCount > 0" class="count-badge count-badge--warn">
          {{ mismatchCount }} need{{ mismatchCount === 1 ? 's' : '' }} writing
        </span>
      </div>

      <div class="toolbar-right">
        <div class="search-box">
          <PhMagnifyingGlass :size="15" />
          <input
            v-model="search"
            type="search"
            class="search-input"
            placeholder="Filter by any value…"
            aria-label="Filter rows"
          />
        </div>

        <label class="toolbar-toggle" title="Show only files a tag write would change">
          <input type="checkbox" v-model="onlyMismatched" />
          <span>Needs writing</span>
        </label>

        <div class="columns-menu" ref="columnsMenuEl">
          <button
            type="button"
            class="toolbar-btn"
            :class="{ active: columnsOpen }"
            :aria-expanded="columnsOpen"
            title="Choose columns"
            @click="columnsOpen = !columnsOpen"
          >
            <PhColumns :size="16" />
            Columns
          </button>
          <div v-if="columnsOpen" class="columns-dropdown">
            <div class="columns-dropdown-actions">
              <button type="button" class="link-btn" @click="showAllColumns">All</button>
              <button type="button" class="link-btn" @click="hideAllColumns">None</button>
            </div>
            <label v-for="column in columns" :key="column.tag" class="columns-option">
              <input
                type="checkbox"
                :checked="visibleTags.includes(column.tag)"
                @change="toggleColumn(column.tag)"
              />
              <span>{{ column.label }}</span>
              <code>{{ column.tag }}</code>
            </label>
          </div>
        </div>

        <button
          type="button"
          class="toolbar-btn"
          :disabled="loading"
          title="Re-read every file's tags from disk"
          @click="load(true)"
        >
          <PhArrowsClockwise :size="16" :class="{ 'ph-spin': loading }" />
          Re-read
        </button>
      </div>
    </div>

    <div v-if="loading" class="tags-state">
      <PhSpinner class="ph-spin state-icon" />
      <p>Reading tags from every file in the library…</p>
      <p class="state-hint">
        The first read probes each file and takes a moment. Later loads come from a cache and are
        instant until a file changes.
      </p>
    </div>

    <div v-else-if="error" class="tags-state tags-state--error">
      <PhWarningCircle class="state-icon" />
      <p>{{ error }}</p>
      <button type="button" class="btn btn-primary" @click="load(false)">Try again</button>
    </div>

    <div v-else-if="rows.length === 0" class="tags-state">
      <PhTag class="state-icon" />
      <p>No audio files in the library yet.</p>
    </div>

    <div v-else-if="visibleRows.length === 0" class="tags-state">
      <PhTag class="state-icon" />
      <p>No file matches this filter.</p>
    </div>

    <div v-else class="tags-scroll" ref="scrollEl" @scroll.passive="onScroll">
      <table class="tags-table" :style="{ width: `${totalWidth}px` }">
        <thead>
          <tr>
            <th
              v-for="column in activeColumns"
              :key="column.key"
              class="tags-th"
              :class="{ 'tags-th--sticky': column.key === 'fileName' }"
              :style="{ width: `${widthFor(column.key)}px` }"
              :aria-sort="ariaSortFor(column.key)"
            >
              <button type="button" class="tags-th-label" @click="sortBy(column.key)">
                <span>{{ column.label }}</span>
                <PhCaretUp v-if="sort.key === column.key && sort.ascending" :size="11" />
                <PhCaretDown v-else-if="sort.key === column.key" :size="11" />
              </button>
              <span
                class="tags-th-grip"
                role="separator"
                aria-orientation="vertical"
                @mousedown="startResize(column.key, $event)"
              ></span>
            </th>
          </tr>
        </thead>

        <tbody>
          <!--
            Windowed rather than fully rendered: twenty columns across a few thousand
            files is tens of thousands of cells, and a table that renders them all takes
            a visible second to scroll. The spacer rows keep the scrollbar honest.
          -->
          <tr v-if="topPadding > 0" class="tags-spacer" :style="{ height: `${topPadding}px` }">
            <td :colspan="activeColumns.length"></td>
          </tr>

          <tr
            v-for="(row, offset) in windowedRows"
            :key="`${row.audiobookId}-${row.fileId}`"
            class="tags-row"
            :class="{
              // Striped by the row's real index, not by :nth-child. The spacer rows flip
              // the parity as the window moves, so a CSS stripe would shimmer on scroll.
              'tags-row--striped': (firstVisibleIndex + offset) % 2 === 1,
              'tags-row--unwritable': !row.writable,
              'tags-row--error': !!row.error,
            }"
            tabindex="0"
            @click="openBook(row)"
            @keydown.enter="openBook(row)"
          >
            <td
              v-for="column in activeColumns"
              :key="column.key"
              class="tags-td"
              :class="cellClass(row, column.key)"
              :style="{ width: `${widthFor(column.key)}px` }"
              :title="cellTitle(row, column.key)"
            >
              {{ cellText(row, column.key) }}
            </td>
          </tr>

          <tr
            v-if="bottomPadding > 0"
            class="tags-spacer"
            :style="{ height: `${bottomPadding}px` }"
          >
            <td :colspan="activeColumns.length"></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="!loading && !error && rows.length > 0" class="tags-legend">
      <span class="legend-item"
        ><i class="swatch swatch--mismatch"></i> differs from Listenarr</span
      >
      <span class="legend-item"><i class="swatch swatch--empty"></i> empty</span>
      <span class="legend-item"
        ><i class="swatch swatch--unwritable"></i> not an M4B — convert first</span
      >
      <span class="legend-spacer"></span>
      <span class="legend-item">Click a row to open its book's Tags tab.</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  PhArrowsClockwise,
  PhCaretDown,
  PhCaretUp,
  PhColumns,
  PhMagnifyingGlass,
  PhSpinner,
  PhTag,
  PhWarningCircle,
} from '@phosphor-icons/vue'
import { apiService } from '@/services/api'
import { logger } from '@/utils/logger'
import type { LibraryTagColumn, LibraryTagRow } from '@/types'

/** The one column that is not a tag: the file itself. */
const FILENAME_KEY = 'fileName'

/**
 * Tags pulled to the front of the catalog's own order.
 *
 * The table opens on every writable tag — the question it answers is whether the library's
 * tags are right, and a default that hid two thirds of them could not answer it. The
 * ordering is the only editorial choice left: the description is the tag this fork exists
 * for, so it sits beside the filename rather than eight columns to the right.
 */
const LEADING_TAGS = ['description']

const ROW_HEIGHT = 30
const OVERSCAN = 12
const MIN_COLUMN_WIDTH = 80
const DEFAULT_COLUMN_WIDTH = 200
const LONG_TEXT_COLUMN_WIDTH = 360
const FILENAME_COLUMN_WIDTH = 380

// Versioned: an earlier build stored a six-column subset, and a browser that had already
// opened the table would otherwise keep it forever and never see the full default.
const VISIBLE_TAGS_KEY = 'listenarr.tagsView.columns.v2'
const WIDTHS_KEY = 'listenarr.tagsView.widths'

const router = useRouter()

const rows = ref<LibraryTagRow[]>([])
const columns = ref<LibraryTagColumn[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

const search = ref('')
const onlyMismatched = ref(false)
const columnsOpen = ref(false)
const columnsMenuEl = ref<HTMLElement | null>(null)

// Empty until the catalog arrives; `load` fills it with every tag unless the browser
// remembers a narrower choice.
const visibleTags = ref<string[]>([])
/**
 * Whether this browser has a remembered column choice at all. Without it, an operator who
 * deliberately hid every column would get all of them back on the next load, because an
 * empty list is indistinguishable from never having chosen.
 */
const columnsChosen = ref(false)
const widths = ref<Record<string, number>>({})
const sort = ref<{ key: string; ascending: boolean }>({ key: FILENAME_KEY, ascending: true })

const scrollEl = ref<HTMLElement | null>(null)
const scrollTop = ref(0)
const viewportHeight = ref(600)

type ActiveColumn = { key: string; label: string }

const columnByTag = computed(() => new Map(columns.value.map((column) => [column.tag, column])))

/*
 * Ordered by the chosen list rather than by the catalog, so the default opens on the
 * description — the tag this whole feature exists for — instead of burying it eight
 * columns to the right. Turning a column on appends it, which is also how a column gets
 * moved: turn it off and back on.
 */
const activeColumns = computed<ActiveColumn[]>(() => [
  { key: FILENAME_KEY, label: 'Filename' },
  ...visibleTags.value
    .map((tag) => columnByTag.value.get(tag))
    .filter((column): column is LibraryTagColumn => !!column)
    .map((column) => ({ key: column.tag, label: column.label })),
])

/** A blurb needs more room than an album name, so a long-text column starts wider. */
const widthFor = (key: string) => {
  const stored = widths.value[key]
  if (stored) return stored
  if (key === FILENAME_KEY) return FILENAME_COLUMN_WIDTH
  return columnByTag.value.get(key)?.isLongText ? LONG_TEXT_COLUMN_WIDTH : DEFAULT_COLUMN_WIDTH
}

const totalWidth = computed(() =>
  activeColumns.value.reduce((total, column) => total + widthFor(column.key), 0),
)

/** A row's value for one column: the filename, or what the file carries for that tag. */
const cellText = (row: LibraryTagRow, key: string) =>
  key === FILENAME_KEY ? row.fileName : (row.tags[key] ?? '')

const isMismatched = (row: LibraryTagRow, key: string) =>
  key !== FILENAME_KEY && row.mismatched.includes(key)

function cellClass(row: LibraryTagRow, key: string) {
  return {
    'tags-td--sticky': key === FILENAME_KEY,
    'tags-td--mismatch': isMismatched(row, key),
    'tags-td--empty': key !== FILENAME_KEY && !row.tags[key],
  }
}

/**
 * The tooltip carries what a truncated cell hides, and — where the two disagree — what
 * Listenarr would put there instead. Reading the full blurb is the whole reason for
 * hovering a description cell.
 */
function cellTitle(row: LibraryTagRow, key: string): string {
  if (key === FILENAME_KEY) {
    return row.error ? `${row.path ?? row.fileName}\n\n${row.error}` : (row.path ?? row.fileName)
  }

  const current = row.tags[key] ?? ''
  const expected = row.expected[key] ?? ''

  if (isMismatched(row, key)) {
    return `Now: ${current || '(empty)'}\n\nListenarr would write: ${expected}`
  }

  return current
}

const searchTerms = computed(() =>
  search.value
    .toLowerCase()
    .split(/\s+/)
    .filter((term) => term.length > 0),
)

/**
 * Filtering searches every column the table can show, not only the visible ones. Hiding
 * a column is a display choice; it should not quietly remove rows from a search.
 */
const filteredRows = computed(() => {
  let result = rows.value

  if (onlyMismatched.value) {
    result = result.filter((row) => row.mismatched.length > 0 || !!row.error)
  }

  if (searchTerms.value.length > 0) {
    result = result.filter((row) => {
      const haystack = [row.fileName, row.bookTitle, ...Object.values(row.tags)]
        .join(' ')
        .toLowerCase()
      return searchTerms.value.every((term) => haystack.includes(term))
    })
  }

  return result
})

const visibleRows = computed(() => {
  const key = sort.value.key
  const direction = sort.value.ascending ? 1 : -1

  return [...filteredRows.value].sort((left, right) => {
    const a = cellText(left, key)
    const b = cellText(right, key)

    // Empty sorts last in either direction: a column is sorted to read the values in
    // it, and a screen of blanks at the top is never what was wanted.
    if (!a && !b) return left.fileName.localeCompare(right.fileName)
    if (!a) return 1
    if (!b) return -1

    const compared = a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' })
    return compared !== 0 ? compared * direction : left.fileName.localeCompare(right.fileName)
  })
})

const mismatchCount = computed(() => rows.value.filter((row) => row.mismatched.length > 0).length)

const firstVisibleIndex = computed(() =>
  Math.max(0, Math.floor(scrollTop.value / ROW_HEIGHT) - OVERSCAN),
)

const lastVisibleIndex = computed(() =>
  Math.min(
    visibleRows.value.length,
    Math.ceil((scrollTop.value + viewportHeight.value) / ROW_HEIGHT) + OVERSCAN,
  ),
)

const windowedRows = computed(() =>
  visibleRows.value.slice(firstVisibleIndex.value, lastVisibleIndex.value),
)

const topPadding = computed(() => firstVisibleIndex.value * ROW_HEIGHT)
const bottomPadding = computed(
  () => Math.max(0, visibleRows.value.length - lastVisibleIndex.value) * ROW_HEIGHT,
)

/**
 * How tall the scroller is, which decides how many rows the window holds. Measured
 * rather than assumed: the table only exists once the load finishes, so a height read
 * at mount would be a guess.
 */
function measureViewport() {
  if (scrollEl.value) viewportHeight.value = scrollEl.value.clientHeight
}

function onScroll(event: Event) {
  const target = event.target as HTMLElement
  scrollTop.value = target.scrollTop
  viewportHeight.value = target.clientHeight
}

function ariaSortFor(key: string) {
  if (sort.value.key !== key) return 'none'
  return sort.value.ascending ? 'ascending' : 'descending'
}

function sortBy(key: string) {
  if (sort.value.key === key) {
    sort.value = { key, ascending: !sort.value.ascending }
    return
  }
  sort.value = { key, ascending: true }
}

function toggleColumn(tag: string) {
  visibleTags.value = visibleTags.value.includes(tag)
    ? visibleTags.value.filter((value) => value !== tag)
    : [...visibleTags.value, tag]
}

/** Every writable tag, description first. Also the table's default. */
function allColumns(): string[] {
  const known = columns.value.map((column) => column.tag)
  const leading = LEADING_TAGS.filter((tag) => known.includes(tag))
  return [...leading, ...known.filter((tag) => !leading.includes(tag))]
}

function showAllColumns() {
  visibleTags.value = allColumns()
}

function hideAllColumns() {
  visibleTags.value = []
}

/* -- Column resizing -------------------------------------------------------- */

let resizingKey: string | null = null
let resizeStartX = 0
let resizeStartWidth = 0

function startResize(key: string, event: MouseEvent) {
  event.preventDefault()
  event.stopPropagation()
  resizingKey = key
  resizeStartX = event.clientX
  resizeStartWidth = widthFor(key)
  window.addEventListener('mousemove', onResizeMove)
  window.addEventListener('mouseup', endResize)
}

function onResizeMove(event: MouseEvent) {
  if (!resizingKey) return
  const next = Math.max(MIN_COLUMN_WIDTH, resizeStartWidth + (event.clientX - resizeStartX))
  widths.value = { ...widths.value, [resizingKey]: next }
}

function endResize() {
  resizingKey = null
  window.removeEventListener('mousemove', onResizeMove)
  window.removeEventListener('mouseup', endResize)
}

/* -- Loading ---------------------------------------------------------------- */

async function load(refresh: boolean) {
  loading.value = true
  error.value = null

  try {
    const table = await apiService.getLibraryTags(refresh)
    columns.value = table.columns
    rows.value = table.rows

    // A stored column list can name a tag the catalog no longer has. Dropping it here
    // beats rendering a column of permanent blanks. With nothing remembered — or nothing
    // left after the drop — the table opens on every tag.
    const known = new Set(table.columns.map((column) => column.tag))
    const kept = visibleTags.value.filter((tag) => known.has(tag))
    visibleTags.value = kept.length > 0 || columnsChosen.value ? kept : allColumns()
  } catch (err) {
    logger.error('Failed to load the library tag table', err)
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
    await nextTick()
    measureViewport()
  }
}

function openBook(row: LibraryTagRow) {
  router.push({ name: 'audiobook-detail', params: { id: row.audiobookId }, query: { tab: 'tags' } })
}

function onDocumentClick(event: MouseEvent) {
  if (!columnsOpen.value) return
  if (columnsMenuEl.value && !columnsMenuEl.value.contains(event.target as Node)) {
    columnsOpen.value = false
  }
}

function restorePreferences() {
  try {
    const storedColumns = localStorage.getItem(VISIBLE_TAGS_KEY)
    if (storedColumns) {
      const parsed = JSON.parse(storedColumns)
      if (Array.isArray(parsed)) {
        visibleTags.value = parsed.filter((value): value is string => typeof value === 'string')
        columnsChosen.value = true
      }
    }

    const storedWidths = localStorage.getItem(WIDTHS_KEY)
    if (storedWidths) {
      const parsed = JSON.parse(storedWidths)
      if (parsed && typeof parsed === 'object') widths.value = parsed as Record<string, number>
    }
  } catch {
    // A browser with storage blocked still gets a working table, just not a remembered one.
  }
}

watch(visibleTags, (value) => {
  columnsChosen.value = true
  try {
    localStorage.setItem(VISIBLE_TAGS_KEY, JSON.stringify(value))
  } catch {}
})

watch(
  widths,
  (value) => {
    try {
      localStorage.setItem(WIDTHS_KEY, JSON.stringify(value))
    } catch {}
  },
  { deep: true },
)

// Scrolling back to the top on a re-filter: the window is an index range, and leaving it
// where it was would show a blank band below a shorter list.
watch([search, onlyMismatched, sort], () => {
  scrollTop.value = 0
  if (scrollEl.value) scrollEl.value.scrollTop = 0
})

onMounted(() => {
  restorePreferences()
  document.addEventListener('click', onDocumentClick)
  window.addEventListener('resize', measureViewport)
  void load(false)
})

onBeforeUnmount(() => {
  document.removeEventListener('click', onDocumentClick)
  window.removeEventListener('resize', measureViewport)
  endResize()
})
</script>

<style scoped>
.tags-view {
  display: flex;
  flex-direction: column;
  height: calc(100dvh - var(--app-top-offset, 60px));
  overflow: hidden;
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-md);
  flex-wrap: wrap;
  padding: var(--spacing-sm) var(--spacing-md);
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--bg-tertiary);
}

.toolbar-left,
.toolbar-right {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
}

.count-badge {
  padding: 2px 8px;
  border-radius: var(--radius-full);
  background: var(--bg-tertiary);
  color: var(--text-secondary);
  font-size: 0.78rem;
  white-space: nowrap;
}

.count-badge--warn {
  background: rgba(255, 165, 0, 0.16);
  color: var(--warning-500);
}

.search-box {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 0 8px;
  height: 30px;
  border: 1px solid var(--bg-tertiary);
  border-radius: var(--radius-md);
  background: var(--bg-primary);
  color: var(--text-muted);
}

.search-input {
  border: none;
  outline: none;
  background: transparent;
  color: var(--text-primary);
  font-size: 0.82rem;
  width: 200px;
}

.toolbar-toggle {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8rem;
  color: var(--text-secondary);
  cursor: pointer;
  white-space: nowrap;
}

.toolbar-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 30px;
  padding: 0 10px;
  border: 1px solid var(--bg-tertiary);
  border-radius: var(--radius-md);
  background: var(--bg-primary);
  color: var(--text-secondary);
  font-size: 0.8rem;
  cursor: pointer;
  transition: var(--transition-fast);
}

.toolbar-btn:hover:not(:disabled),
.toolbar-btn.active {
  color: var(--text-primary);
  border-color: var(--brand-500);
}

.toolbar-btn:disabled {
  opacity: 0.6;
  cursor: default;
}

.columns-menu {
  position: relative;
}

.columns-dropdown {
  position: absolute;
  right: 0;
  top: calc(100% + 4px);
  z-index: 30;
  width: 260px;
  max-height: 60vh;
  overflow-y: auto;
  padding: var(--spacing-xs);
  background: var(--bg-secondary);
  border: 1px solid var(--bg-tertiary);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-lg);
}

.columns-dropdown-actions {
  display: flex;
  gap: var(--spacing-sm);
  padding: 4px 8px 8px;
  border-bottom: 1px solid var(--bg-tertiary);
  margin-bottom: 4px;
}

.link-btn {
  background: none;
  border: none;
  padding: 0;
  color: var(--brand-400);
  font-size: 0.78rem;
  cursor: pointer;
}

.columns-option {
  display: grid;
  grid-template-columns: auto 1fr auto;
  align-items: center;
  gap: var(--spacing-sm);
  padding: 5px 8px;
  border-radius: var(--radius-sm);
  font-size: 0.82rem;
  color: var(--text-secondary);
  cursor: pointer;
}

.columns-option:hover {
  background: var(--bg-tertiary);
}

.columns-option code {
  font-size: 0.7rem;
  color: var(--text-muted);
}

.tags-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: var(--spacing-sm);
  flex: 1;
  padding: var(--spacing-xl);
  color: var(--text-secondary);
  text-align: center;
}

.tags-state--error {
  color: var(--danger-500);
}

.state-icon {
  font-size: 2rem;
}

.state-hint {
  max-width: 44ch;
  color: var(--text-muted);
  font-size: 0.82rem;
}

.tags-scroll {
  flex: 1;
  overflow: auto;
  background: var(--bg-primary);
}

.tags-table {
  border-collapse: separate;
  border-spacing: 0;
  table-layout: fixed;
  font-size: 0.8rem;
}

.tags-th {
  position: sticky;
  top: 0;
  z-index: 2;
  padding: 0;
  background: var(--bg-tertiary);
  border-right: 1px solid var(--bg-primary);
  border-bottom: 1px solid var(--bg-primary);
  text-align: left;
  font-weight: 600;
  color: var(--text-secondary);
  white-space: nowrap;
}

.tags-th--sticky {
  left: 0;
  z-index: 3;
}

.tags-th-label {
  display: flex;
  align-items: center;
  gap: 4px;
  width: 100%;
  height: 30px;
  padding: 0 8px;
  border: none;
  background: none;
  color: inherit;
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.tags-th-grip {
  position: absolute;
  top: 0;
  right: 0;
  width: 5px;
  height: 100%;
  cursor: col-resize;
  user-select: none;
}

.tags-th-grip:hover {
  background: var(--brand-500);
}

.tags-row {
  cursor: pointer;
}

.tags-row--striped .tags-td {
  background: var(--bg-secondary);
}

.tags-row:hover .tags-td {
  background: var(--bg-tertiary);
}

.tags-row:focus-visible {
  outline: 2px solid var(--brand-focus);
  outline-offset: -2px;
}

.tags-td {
  height: 30px;
  max-width: 0;
  padding: 0 8px;
  background: var(--bg-primary);
  border-right: 1px solid var(--bg-secondary);
  border-bottom: 1px solid var(--bg-secondary);
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.tags-td--sticky {
  position: sticky;
  left: 0;
  z-index: 1;
  color: var(--text-primary);
}

/* The sticky column has to repaint its own stripe: the row's background sits behind it. */
.tags-row--striped .tags-td--sticky {
  background: var(--bg-secondary);
}

.tags-row:hover .tags-td--sticky {
  background: var(--bg-tertiary);
}

.tags-td--mismatch {
  color: var(--warning-500);
  box-shadow: inset 2px 0 0 var(--warning-500);
}

.tags-td--empty {
  background-image: repeating-linear-gradient(
    45deg,
    transparent,
    transparent 6px,
    rgba(255, 255, 255, 0.035) 6px,
    rgba(255, 255, 255, 0.035) 12px
  );
}

.tags-row--unwritable .tags-td--sticky {
  box-shadow: inset 3px 0 0 var(--text-disabled);
}

.tags-row--error .tags-td--sticky {
  color: var(--danger-500);
  box-shadow: inset 3px 0 0 var(--danger-500);
}

.tags-spacer td {
  padding: 0;
  border: none;
  background: var(--bg-primary);
}

.tags-legend {
  display: flex;
  align-items: center;
  gap: var(--spacing-md);
  flex-wrap: wrap;
  padding: 6px var(--spacing-md);
  background: var(--bg-secondary);
  border-top: 1px solid var(--bg-tertiary);
  color: var(--text-muted);
  font-size: 0.75rem;
}

.legend-item {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.legend-spacer {
  flex: 1;
}

.swatch {
  width: 10px;
  height: 10px;
  border-radius: 2px;
  display: inline-block;
}

.swatch--mismatch {
  background: var(--warning-500);
}

.swatch--empty {
  background: var(--bg-surface);
}

.swatch--unwritable {
  background: var(--text-disabled);
}

@media (max-width: 768px) {
  .search-input {
    width: 120px;
  }
}
</style>

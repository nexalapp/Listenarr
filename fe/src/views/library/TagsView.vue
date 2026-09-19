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
        <span
          v-if="mismatchCount > 0"
          class="count-badge count-badge--warn"
          title="Files carrying a tag a write would change"
        >
          {{ mismatchCount }} mistagged
        </span>
        <span
          v-if="organizeCount > 0"
          class="count-badge count-badge--warn"
          title="Files an organize would move or rename"
        >
          {{ organizeCount }} misorganized
        </span>
        <span
          v-if="chapterIssueCount > 0"
          class="count-badge count-badge--warn"
          title="Files whose chapter atom is broken or whose chapters are CD tracks"
        >
          {{ chapterIssueCount }} bad chapters
        </span>
        <span v-if="selectedFiles.size > 0" class="count-badge count-badge--selected">
          {{ selectedFiles.size }} file{{ selectedFiles.size === 1 ? '' : 's' }} selected
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

        <label
          class="toolbar-toggle"
          title="Show only files a tag write or an organize would change"
        >
          <input type="checkbox" v-model="onlyMismatched" />
          <span>Needs work</span>
        </label>

        <!--
          Chapter verdicts are their own filter rather than part of "needs work": nothing
          on this page writes chapters yet, and mixing a file that needs a chapter repair
          into a list of files a tag write would fix would make the Apply button lie.
        -->
        <select
          v-model="chapterFilter"
          class="toolbar-select"
          aria-label="Filter by chapter health"
          title="Show only files whose chapters have this verdict"
        >
          <option v-for="option in CHAPTER_FILTERS" :key="option.value" :value="option.value">
            {{ option.label }}
          </option>
        </select>

        <!--
          Second, so the first `.toolbar-toggle` stays the filter. Every row is a fixed
          height, so a second line costs one constant rather than teaching the windowing
          about rows that vary.
        -->
        <label
          class="toolbar-toggle"
          title="Show what a write or an organize would put in each cell"
        >
          <input type="checkbox" v-model="showProposals" />
          <span>Proposals</span>
        </label>

        <!--
          One action, and its label says what it covers. Two buttons beside a tick column
          that sits left of every other column read as two things the tick might mean;
          the tick means "these books" and this says which of their parts it reaches.
        -->
        <div class="apply-menu" ref="applyMenuEl">
          <button
            type="button"
            class="toolbar-btn apply-btn"
            :disabled="!canApply"
            :title="applyHint"
            @click="applySelection"
          >
            <PhCheck :size="16" />
            {{ applyLabel }}
          </button>
          <button
            type="button"
            class="toolbar-btn apply-caret"
            :class="{ active: applyOpen }"
            :aria-expanded="applyOpen"
            aria-label="Choose what Apply covers"
            title="Choose what Apply covers"
            @click="applyOpen = !applyOpen"
          >
            <PhCaretDown :size="12" />
          </button>

          <div v-if="applyOpen" class="columns-dropdown apply-dropdown">
            <label class="columns-option">
              <input type="checkbox" v-model="applyTags" />
              <span>Tags</span>
              <code>written into the files</code>
            </label>
            <label class="columns-option">
              <input type="checkbox" v-model="applyPaths" />
              <span>Paths</span>
              <code>moved and renamed</code>
            </label>
          </div>
        </div>

        <!--
          Its own button rather than a third Apply scope: a repair is not a write of
          Listenarr's values into the file, it is a rebuild of the file's own structure,
          and it only applies to the rows whose Chapters cell is red.
        -->
        <button
          v-if="repairableSelection.length > 0"
          type="button"
          class="toolbar-btn"
          :disabled="working"
          :title="`Rebuild the chapters of ${repairableSelection.length} selected file(s)`"
          @click="repairOpen = true"
        >
          <PhListNumbers :size="16" />
          Repair chapters ({{ repairableSelection.length }})
        </button>

        <button
          v-if="selectedBookIds.size > 0"
          type="button"
          class="toolbar-btn"
          :disabled="working"
          :title="`Hear the opening and closing credits of ${selectedBookIds.size} selected book(s) and check them against the record`"
          @click="auditSelected"
        >
          <PhEar :size="16" />
          Listen ({{ selectedBookIds.size }})
        </button>

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
            <label class="columns-option">
              <input type="checkbox" v-model="showPath" />
              <span>Path</span>
              <code>where the file is</code>
            </label>
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
          :disabled="loading || refreshing"
          title="Re-read every file's tags from disk"
          @click="load(true)"
        >
          <PhArrowsClockwise :size="16" :class="{ 'ph-spin': refreshing }" />
          Re-read
        </button>
      </div>

      <!--
        Its own row, under everything. Inline with the badges it grew into the buttons'
        space, and a long refusal pushed the right-hand group onto a second line.
      -->
      <p v-if="actionMessage" class="toolbar-message" role="status">{{ actionMessage }}</p>
    </div>

    <div v-if="loading" class="tags-state">
      <PhSpinner class="ph-spin state-icon" />
      <p>Reading tags from every file in the library…</p>
      <p class="state-hint">
        The first read probes each file and takes a moment. Later loads come from a cache and are
        instant until a file changes.
      </p>
    </div>

    <div v-else-if="error && rows.length === 0" class="tags-state tags-state--error">
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
      <table
        class="tags-table"
        :class="{ 'tags-table--proposals': showProposals }"
        :style="tableStyle"
      >
        <thead>
          <tr>
            <th
              v-for="column in activeColumns"
              :key="column.key"
              class="tags-th"
              :class="headerClass(column.key)"
              :style="{ width: `${widthFor(column.key)}px` }"
              :aria-sort="ariaSortFor(column.key)"
            >
              <!--
                The control headings are controls, not labels: they have nothing to sort
                by and nothing to resize, and a sort button over a checkbox would swallow
                the click that was meant for it. The transport column has no heading at
                all — there is nothing to say about a column of play buttons.
              -->
              <template v-if="column.key === AUDIO_KEY"></template>

              <template v-else-if="column.key === SELECT_KEY">
                <input
                  type="checkbox"
                  class="row-select"
                  :checked="allVisibleSelected"
                  :indeterminate.prop="someVisibleSelected && !allVisibleSelected"
                  :title="allVisibleSelected ? 'Clear the selection' : 'Select every file listed'"
                  aria-label="Select every file listed"
                  @change="toggleAllVisible"
                />
              </template>

              <template v-else>
                <button
                  type="button"
                  class="tags-th-label"
                  :title="headerHint(column.key)"
                  @click="sortBy(column.key)"
                >
                  <span>{{ column.label }}</span>
                  <PhCaretUp v-if="sort.key === column.key && sort.ascending" :size="11" />
                  <PhCaretDown v-else-if="sort.key === column.key" :size="11" />
                </button>

                <!--
                  Locking a tag across a selection in one click, which is the whole reason
                  the lock is worth having on a table rather than only on a book page.
                -->
                <button
                  v-if="isLockableColumn(column.key)"
                  type="button"
                  class="th-lock"
                  :class="{ 'th-lock--on': columnLocked(column.key) }"
                  :disabled="selectedFiles.size === 0 || working"
                  :title="
                    selectedFiles.size === 0
                      ? `Select books first to lock ${columnLockNoun(column.key)} on them`
                      : columnLocked(column.key)
                        ? `Unlock ${columnLockNoun(column.key)} on the selected books`
                        : `Lock ${columnLockNoun(column.key)} on the selected books`
                  "
                  @click="toggleColumnLock(column.key)"
                >
                  <svg class="lock-icon" aria-hidden="true">
                    <use
                      :href="columnLocked(column.key) ? '#tags-lock-closed' : '#tags-lock-open'"
                    />
                  </svg>
                </button>

                <span
                  class="tags-th-grip"
                  role="separator"
                  aria-orientation="vertical"
                  @mousedown="startResize(column.key, $event)"
                ></span>
              </template>
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
              'tags-row--busy': isBusy(row),
            }"
            :aria-busy="isBusy(row) || undefined"
            :title="isBusy(row) ? busyHint(row) : undefined"
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
              @click="column.key === SELECT_KEY ? selectFromCell($event, row) : undefined"
            >
              <!--
                One tick per file. The job carries the file scope, so ticking one part
                of a four-part collection writes that part and leaves the rest alone.
              -->
              <input
                v-if="column.key === SELECT_KEY"
                type="checkbox"
                class="row-select"
                :checked="selectedFiles.has(row.fileId)"
                :disabled="isBusy(row)"
                :aria-label="`Select ${row.fileName}`"
                @click.stop="tickFrom($event, row)"
                @keydown.stop
              />

              <button
                v-else-if="column.key === AUDIO_KEY"
                type="button"
                class="row-play"
                :class="{ 'row-play--on': playingFileId === row.fileId }"
                :aria-label="
                  playingFileId === row.fileId ? `Stop ${row.fileName}` : `Play ${row.fileName}`
                "
                :aria-pressed="playingFileId === row.fileId"
                @click.stop="togglePlay(row)"
                @keydown.stop
              >
                <svg class="play-icon" aria-hidden="true">
                  <use :href="playingFileId === row.fileId ? '#tags-pause' : '#tags-play'" />
                </svg>
              </button>

              <!--
                The padlock leads the value rather than trailing it: trailing, it lands
                against the next column's text on a truncated cell and reads as belonging
                to whichever value it happens to touch. Leading, each column gets one rail
                of them — and it sits beside both lines rather than on the first, so a
                proposal lines up under the value it would replace.
              -->
              <div v-else class="cell">
                <!--
                  While the row is busy the filename's padlock becomes a spinner: same
                  16px slot, so nothing else moves. The lock cannot be toggled mid-write
                  anyway.
                -->
                <span
                  v-if="column.key === FILENAME_KEY && isBusy(row)"
                  class="cell-lock cell-busy"
                  :title="busyHint(row)"
                  aria-hidden="true"
                ></span>
                <button
                  v-else-if="isLockableColumn(column.key)"
                  type="button"
                  class="cell-lock"
                  :class="{ 'cell-lock--on': isLocked(row, column.key) }"
                  :title="lockHint(row, column.key)"
                  :aria-pressed="isLocked(row, column.key)"
                  @click.stop="toggleLock(row, column.key)"
                  @keydown.stop
                >
                  <svg class="lock-icon" aria-hidden="true">
                    <use
                      :href="isLocked(row, column.key) ? '#tags-lock-closed' : '#tags-lock-open'"
                    />
                  </svg>
                </button>
                <div class="cell-lines">
                  <span class="cell-text">{{ cellText(row, column.key) }}</span>
                  <span
                    v-if="showProposals && proposalFor(row, column.key)"
                    class="cell-proposal"
                    >{{ proposalFor(row, column.key) }}</span
                  >
                </div>
              </div>
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
      <span class="legend-item">
        <svg class="lock-icon" aria-hidden="true"><use href="#tags-lock-closed" /></svg>
        locked — no write may touch it
      </span>
      <span class="legend-spacer"></span>
      <span class="legend-item">Click a row to open its book's Tags tab.</span>
    </div>

    <!--
      One element for the whole table. It is never shown: the row buttons are the
      transport, and a second set of controls that could disagree with them would be one
      set too many.
    -->
    <audio ref="audioEl" preload="none" @ended="onPlaybackEnded" @error="onPlaybackError"></audio>

    <RenamePreviewModal
      v-if="organizeOpen"
      :visible="organizeOpen"
      :audiobookIds="[...selectedBookIds]"
      @close="organizeOpen = false"
      @done="onOrganized"
    />

    <ChapterRepairModal
      v-if="repairOpen"
      :visible="repairOpen"
      :scopes="repairScopes"
      @close="repairOpen = false"
      @confirm="repairSelected"
    />

    <!--
      One sprite for the whole table rather than an icon component per cell: a padlock on
      every cell of a windowed table is several hundred of them, and several hundred
      component instances is the difference between a table that scrolls and one that
      stutters.
    -->
    <svg class="icon-sprite" aria-hidden="true" focusable="false">
      <symbol id="tags-lock-closed" viewBox="0 0 16 16">
        <path
          d="M5.5 7.5V5.5a2.5 2.5 0 0 1 5 0v2"
          fill="none"
          stroke="currentColor"
          stroke-width="1.4"
        />
        <rect x="3.4" y="7.4" width="9.2" height="6.2" rx="1.4" fill="currentColor" />
      </symbol>
      <symbol id="tags-play" viewBox="0 0 16 16">
        <path d="M5 3.6v8.8l7-4.4z" fill="currentColor" />
      </symbol>
      <symbol id="tags-pause" viewBox="0 0 16 16">
        <rect x="4.4" y="3.6" width="2.6" height="8.8" rx="0.6" fill="currentColor" />
        <rect x="9" y="3.6" width="2.6" height="8.8" rx="0.6" fill="currentColor" />
      </symbol>
      <symbol id="tags-lock-open" viewBox="0 0 16 16">
        <path
          d="M5.5 7.5V5.5a2.5 2.5 0 0 1 4.9-.6"
          fill="none"
          stroke="currentColor"
          stroke-width="1.4"
        />
        <rect
          x="3.4"
          y="7.4"
          width="9.2"
          height="6.2"
          rx="1.4"
          fill="none"
          stroke="currentColor"
          stroke-width="1.4"
        />
      </symbol>
    </svg>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  PhArrowsClockwise,
  PhCaretDown,
  PhCaretUp,
  PhCheck,
  PhColumns,
  PhEar,
  PhListNumbers,
  PhMagnifyingGlass,
  PhSpinner,
  PhTag,
  PhWarningCircle,
} from '@phosphor-icons/vue'
import RenamePreviewModal from '@/components/domain/organize/RenamePreviewModal.vue'
import ChapterRepairModal from '@/components/domain/tagging/ChapterRepairModal.vue'
import type { ChapterRepairScope } from '@/components/domain/tagging/ChapterRepairModal.vue'
import { apiService } from '@/services/api'
import { useTagJobsStore } from '@/stores/tagJobs'
import { logger } from '@/utils/logger'
import type {
  AudioAuditVerdict,
  ChapterHealth,
  LibraryTagColumn,
  LibraryTagRow,
  RenameOperation,
} from '@/types'

/**
 * The columns that are not tags: which books an action is for, the file, and where it
 * sits. Prefixed so nothing the catalog could ever add collides with them.
 */
const SELECT_KEY = '__select'
const AUDIO_KEY = '__audio'
const PATH_KEY = '__path'
const CHAPTERS_KEY = '__chapters'
const AUDIT_KEY = '__audit'

const AUDIO_AUDIT_LABELS: Record<AudioAuditVerdict, string> = {
  match: 'Matches',
  'narrator-mismatch': 'Narrator differs',
  mismatch: 'Different book',
  inconclusive: 'Unclear',
}
const FILENAME_KEY = 'fileName'

/** How each chapter verdict reads in a cell, a filter and a tooltip. */
const CHAPTER_HEALTH_LABELS: Record<ChapterHealth, string> = {
  unknown: '',
  healthy: 'Healthy',
  corrupt: 'Corrupt',
  oversegmented: 'Over-segmented',
  'generic-titles': 'Generic titles',
  none: 'No chapters',
}

type ChapterFilter = 'all' | 'issues' | ChapterHealth | 'audio-mismatch' | 'audio-unheard'

const CHAPTER_FILTERS: { value: ChapterFilter; label: string }[] = [
  { value: 'all', label: 'Any chapters' },
  { value: 'issues', label: 'Chapter issues' },
  { value: 'corrupt', label: 'Corrupt' },
  { value: 'oversegmented', label: 'Over-segmented' },
  { value: 'generic-titles', label: 'Generic titles' },
  { value: 'none', label: 'No chapters' },
  { value: 'healthy', label: 'Healthy' },
  { value: 'audio-mismatch', label: 'Audio mismatch' },
  { value: 'audio-unheard', label: 'Not listened to' },
]

/** The verdicts worth a repair, as opposed to a retitle or a shrug. */
const CHAPTER_ISSUES: ReadonlySet<ChapterHealth> = new Set(['corrupt', 'oversegmented'])

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

/**
 * How tall a row is once it carries a proposal underneath.
 *
 * Uniform rather than per-row: a row that grew only where something changed would make
 * the window's arithmetic a running total instead of a multiplication, and the whole
 * reason this table scrolls is that the arithmetic is a multiplication.
 */
const PROPOSAL_ROW_HEIGHT = 48 // 4px above, two 20px lines, 4px below
const OVERSCAN = 12
const MIN_COLUMN_WIDTH = 80
const DEFAULT_COLUMN_WIDTH = 200
const LONG_TEXT_COLUMN_WIDTH = 360
const FILENAME_COLUMN_WIDTH = 380
const PATH_COLUMN_WIDTH = 320
const CHAPTERS_COLUMN_WIDTH = 150
const AUDIT_COLUMN_WIDTH = 130

/** Wide enough for a checkbox and nothing else; not resizable, so it is not a preference. */
const SELECT_COLUMN_WIDTH = 34

/** Likewise: one button, no scrubber, no duration. */
const AUDIO_COLUMN_WIDTH = 30

// Versioned: an earlier build stored a six-column subset, and a browser that had already
// opened the table would otherwise keep it forever and never see the full default.
const VISIBLE_TAGS_KEY = 'listenarr.tagsView.columns.v2'
const WIDTHS_KEY = 'listenarr.tagsView.widths'
const SHOW_PATH_KEY = 'listenarr.tagsView.showPath'
const PROPOSALS_KEY = 'listenarr.tagsView.proposals'
const APPLY_SCOPE_KEY = 'listenarr.tagsView.applyScope'
const NEEDS_WORK_KEY = 'listenarr.tagsView.needsWork'
const CHAPTER_FILTER_KEY = 'listenarr.tagsView.chapterFilter'
const SORT_KEY = 'listenarr.tagsView.sort'

const router = useRouter()

// The last table this session loaded, kept across navigations so the page opens on it
// at once and refreshes underneath. A module-level cache rather than a store: nothing
// else reads it, and it is meant to be replaced whole, never patched.
type CachedTable = { columns: LibraryTagColumn[]; rows: LibraryTagRow[] }
let lastTable = null as CachedTable | null

const rows = ref<LibraryTagRow[]>(lastTable?.rows ?? [])
const columns = ref<LibraryTagColumn[]>(lastTable?.columns ?? [])
// `loading` blanks the page and only applies when there is nothing to show yet;
// `refreshing` spins the Re-read icon over a table that stays put.
const loading = ref(lastTable === null)
const refreshing = ref(false)
const error = ref<string | null>(null)

/* -- Rows with work in flight ------------------------------------------------- */

const tagJobsStore = useTagJobsStore()

// Books being organized by this page, held while the request runs. Tag writes are
// tracked by the store instead: they are queued jobs whose progress arrives over
// SignalR, so a row stays busy until the job ends whichever page queued it.
const organizingBookIds = ref<Set<number>>(new Set())

const busyBookIds = computed(() => {
  const ids = new Set(organizingBookIds.value)
  for (const job of tagJobsStore.activeJobs) ids.add(job.audiobookId)
  return ids
})

const isBusy = (row: LibraryTagRow) => busyBookIds.value.has(row.audiobookId)

function busyHint(row: LibraryTagRow) {
  if (organizingBookIds.value.has(row.audiobookId)) return 'Organizing…'
  const job = tagJobsStore.getJobForAudiobook(row.audiobookId)
  return job?.status === 'Running' ? 'Writing tags…' : 'Queued for tag writing…'
}

/**
 * Replace one book's rows with what the server says now, leaving every other row —
 * and the operator's scroll, selection and sort — exactly where they were. Rows keep
 * their positions; a book whose files changed count is trimmed or grown in place.
 */
async function refreshBooks(audiobookIds: number[]) {
  if (audiobookIds.length === 0) return
  try {
    const table = await apiService.getLibraryTags(false, audiobookIds)
    const fresh = new Map<number, LibraryTagRow[]>()
    for (const row of table.rows.map(normalizeRow)) {
      const list = fresh.get(row.audiobookId)
      if (list) list.push(row)
      else fresh.set(row.audiobookId, [row])
    }

    const next: LibraryTagRow[] = []
    const placed = new Set<number>()
    for (const row of rows.value) {
      const replacement = fresh.get(row.audiobookId)
      if (!replacement) {
        next.push(row)
      } else if (!placed.has(row.audiobookId)) {
        placed.add(row.audiobookId)
        next.push(...replacement)
      }
    }
    rows.value = next

    // A file the write replaced has a new row; the tick was on the old one.
    const known = new Set(rows.value.map((row) => row.fileId))
    if ([...selectedFiles.value].some((fileId) => !known.has(fileId))) {
      selectedFiles.value = new Set([...selectedFiles.value].filter((fileId) => known.has(fileId)))
    }
  } catch (err) {
    logger.warn('Could not refresh rows after a job finished', err)
  }
}

// When a tag job leaves the active set, its book's rows are re-read. Tracked by id
// rather than by status so a job that is dismissed, not just completed, counts too.
let previouslyActive = new Set<number>()
watch(
  () => tagJobsStore.activeJobs.map((job) => job.audiobookId),
  (activeIds) => {
    const current = new Set(activeIds)
    const finished = [...previouslyActive].filter((id) => !current.has(id))
    previouslyActive = current
    if (finished.length > 0 && rows.value.length > 0) void refreshBooks(finished)
  },
  { immediate: true },
)

const search = ref('')
const onlyMismatched = ref(false)
const chapterFilter = ref<ChapterFilter>('all')
const columnsOpen = ref(false)
const columnsMenuEl = ref<HTMLElement | null>(null)
const showPath = ref(true)

/**
 * Whether each cell also shows what Listenarr would put there.
 *
 * Off by default: the dense reading of what the library actually carries is the view
 * this table is opened for most often, and halving how many files fit on a screen is not
 * a cost to impose on it.
 */
const showProposals = ref(false)

/**
 * Which files the toolbar's actions are for, by file id.
 *
 * By file, because a book's parts are not always the same work: a collection of short
 * stories arrives as one title whose files carry different names, and writing tags into
 * one of them should not touch the other three. The tag job carries the file scope, so
 * a tick means exactly what it looks like it means.
 *
 * Organizing still resolves to whole books, because moving a folder moves everything in
 * it — the modal shows which books a path change would reach before anything moves.
 */
const selectedFiles = ref<Set<number>>(new Set())

/** The row a shift-click measures from: the last one ticked without one. */
const rangeAnchorFileId = ref<number | null>(null)
const organizeOpen = ref(false)
const applyOpen = ref(false)
const applyMenuEl = ref<HTMLElement | null>(null)

/**
 * Which parts of the selected books Apply reaches.
 *
 * Tags on, paths off: writing tags is the page's subject, and moving several hundred
 * files is not something a default should arrange. The label restates whichever is on,
 * so the scope is legible without opening the menu.
 */
const applyTags = ref(true)
const applyPaths = ref(false)

/**
 * Which row is playing, if any.
 *
 * One shared <audio> rather than one per row: only one file can usefully play at a
 * time, and a windowed table would otherwise build and tear down a media element every
 * time a row scrolled past.
 */
const playingFileId = ref<number | null>(null)
const audioEl = ref<HTMLAudioElement | null>(null)
const working = ref(false)
const actionMessage = ref<string | null>(null)

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
  { key: SELECT_KEY, label: '' },
  { key: AUDIO_KEY, label: '' },
  { key: FILENAME_KEY, label: 'Filename' },
  ...(showPath.value ? [{ key: PATH_KEY, label: 'Path' }] : []),
  { key: CHAPTERS_KEY, label: 'Chapters' },
  { key: AUDIT_KEY, label: 'Audio' },
  ...visibleTags.value
    .map((tag) => columnByTag.value.get(tag))
    .filter((column): column is LibraryTagColumn => !!column)
    .map((column) => ({ key: column.tag, label: column.label })),
])

/** Whether a column holds a tag, as opposed to the selection, the file or its path. */
const isTagColumn = (key: string) =>
  key !== SELECT_KEY &&
  key !== AUDIO_KEY &&
  key !== FILENAME_KEY &&
  key !== PATH_KEY &&
  key !== CHAPTERS_KEY &&
  key !== AUDIT_KEY

/**
 * Which columns carry a padlock. The path is lockable for the same reason a tag is —
 * something about the file is deliberate and no rule should overwrite it — though what
 * honours it is organizing rather than the tag writer.
 */
const isLockableColumn = (key: string) => isTagColumn(key) || key === FILENAME_KEY

/**
 * What a padlock will do, spelled out. A path lock and a tag lock look identical and
 * mean different things — one stops the tag writer, the other stops organizing — so the
 * button has to say which.
 */
/** What the bulk padlock over a column heading locks, named for a tooltip. */
const columnLockNoun = (key: string) =>
  key === FILENAME_KEY ? "these files' paths" : (columnByTag.value.get(key)?.label ?? key)

function lockHint(row: LibraryTagRow, key: string) {
  if (key === FILENAME_KEY) {
    return isLocked(row, key)
      ? 'Locked: organizing may not rename this file or move its folder. Click to release it.'
      : "Lock this file's path so organizing leaves its name, and its book's folder, alone."
  }

  return isLocked(row, key)
    ? 'Locked: no write may touch this tag on this file. Click to release it.'
    : 'Lock this tag on this file so no write may touch it.'
}

/** A blurb needs more room than an album name, so a long-text column starts wider. */
const widthFor = (key: string) => {
  if (key === SELECT_KEY) return SELECT_COLUMN_WIDTH
  if (key === AUDIO_KEY) return AUDIO_COLUMN_WIDTH
  const stored = widths.value[key]
  if (stored) return stored
  if (key === FILENAME_KEY) return FILENAME_COLUMN_WIDTH
  if (key === PATH_KEY) return PATH_COLUMN_WIDTH
  if (key === CHAPTERS_KEY) return CHAPTERS_COLUMN_WIDTH
  if (key === AUDIT_KEY) return AUDIT_COLUMN_WIDTH
  return columnByTag.value.get(key)?.isLongText ? LONG_TEXT_COLUMN_WIDTH : DEFAULT_COLUMN_WIDTH
}

const totalWidth = computed(() =>
  activeColumns.value.reduce((total, column) => total + widthFor(column.key), 0),
)

/**
 * The filename column is frozen beside the selection column rather than at the very
 * left, so the offset is a fact about the table and belongs to it rather than being
 * repeated as a magic number in the stylesheet.
 */
const rowHeight = computed(() => (showProposals.value ? PROPOSAL_ROW_HEIGHT : ROW_HEIGHT))

const tableStyle = computed(() => ({
  width: `${totalWidth.value}px`,
  '--select-width': `${SELECT_COLUMN_WIDTH}px`,
  '--audio-width': `${AUDIO_COLUMN_WIDTH}px`,
  '--row-height': `${rowHeight.value}px`,
}))

/** A row's value for one column: the file, where it sits, or what it carries for a tag. */
function cellText(row: LibraryTagRow, key: string) {
  if (key === SELECT_KEY || key === AUDIO_KEY) return ''
  if (key === FILENAME_KEY) return row.fileName
  if (key === PATH_KEY) return row.displayPath ?? row.path ?? ''
  if (key === CHAPTERS_KEY) return chapterText(row)
  if (key === AUDIT_KEY) return row.audioAudit ? AUDIO_AUDIT_LABELS[row.audioAudit] : ''
  return row.tags[key] ?? ''
}

const hasAudioIssue = (row: LibraryTagRow) =>
  row.audioAudit === 'mismatch' || row.audioAudit === 'narrator-mismatch'

/**
 * Verdict first, count second, so sorting the column groups the corrupt files together
 * and orders each group by how many marks it has. Unknown is empty and sorts last.
 */
function chapterText(row: LibraryTagRow) {
  const label = CHAPTER_HEALTH_LABELS[row.chapterHealth]
  return label ? `${label} · ${row.chapterCount}` : ''
}

const hasChapterIssue = (row: LibraryTagRow) => CHAPTER_ISSUES.has(row.chapterHealth)

/** What a column is, for the heading's tooltip; the two judged columns say when they are judged. */
function headerHint(key: string): string | undefined {
  if (key === CHAPTERS_KEY) {
    return 'Each M4B’s chapter atom and chapter track, judged when this table loads. Re-read re-inspects the files; tick rows and press Repair chapters to fix them.'
  }
  if (key === AUDIT_KEY) {
    return 'Whether the audio introduces itself as this book. Tick rows and press Listen to hear the credits; needs transcription on in Settings.'
  }
  return undefined
}

const isLocked = (row: LibraryTagRow, key: string) =>
  key === FILENAME_KEY ? row.pathLocked : isTagColumn(key) && row.lockedTags.includes(key)

/**
 * Whether a cell disagrees with Listenarr in a way that still matters.
 *
 * A locked tag is excluded rather than filtered out on the server: the lock is applied
 * the moment it is clicked, and a cell that stayed yellow until the next full re-read
 * would leave the operator unsure whether the click had taken.
 */
const isMismatched = (row: LibraryTagRow, key: string) => {
  // The one path lock covers both halves, so it clears both cells.
  if (key === PATH_KEY) return row.pathMismatched && !row.pathLocked
  if (key === FILENAME_KEY) return row.fileNameMismatched && !row.pathLocked
  return isTagColumn(key) && row.mismatched.includes(key) && !isLocked(row, key)
}

/**
 * What Listenarr would put in this cell, or empty when it would leave it alone.
 *
 * Keyed off the same predicate that paints the cell yellow, so a locked cell shows no
 * proposal — nothing would be written there, and offering a value that will never be
 * applied is the one thing a proposal column must not do.
 */
function proposalFor(row: LibraryTagRow, key: string) {
  if (!isMismatched(row, key)) return ''
  if (key === FILENAME_KEY) return row.expectedFileName ?? ''
  if (key === PATH_KEY) return row.expectedPath ?? ''
  return row.expected[key] ?? ''
}

function cellClass(row: LibraryTagRow, key: string) {
  return {
    'tags-td--frozen': key === SELECT_KEY || key === AUDIO_KEY || key === FILENAME_KEY,
    'tags-td--select': key === SELECT_KEY,
    'tags-td--audio': key === AUDIO_KEY,
    'tags-td--sticky': key === FILENAME_KEY,
    'tags-td--mismatch': isMismatched(row, key),
    // Red: a problem no repair can fix yet. Amber: a problem a repair can fix.
    'tags-td--chapters-issue':
      (key === CHAPTERS_KEY &&
        REPAIRABLE_HEALTH.has(row.chapterHealth) &&
        !row.chapterRepairable &&
        !row.chapterPlanPending) ||
      (key === AUDIT_KEY && hasAudioIssue(row)),
    'tags-td--chapters-fixable':
      key === CHAPTERS_KEY &&
      REPAIRABLE_HEALTH.has(row.chapterHealth) &&
      (!!row.chapterRepairable || !!row.chapterPlanPending),
    'tags-td--chapters-note':
      (key === CHAPTERS_KEY && row.chapterHealth === 'none') ||
      (key === AUDIT_KEY && row.audioAudit === 'inconclusive'),
    'tags-td--locked': isLocked(row, key),
    'tags-td--empty': isTagColumn(key) && !row.tags[key],
  }
}

function headerClass(key: string) {
  return {
    'tags-th--frozen': key === SELECT_KEY || key === AUDIO_KEY || key === FILENAME_KEY,
    'tags-th--select': key === SELECT_KEY,
    'tags-th--audio': key === AUDIO_KEY,
    'tags-th--sticky': key === FILENAME_KEY,
  }
}

/**
 * The tooltip carries what a truncated cell hides, and — where the two disagree — what
 * Listenarr would put there instead. Reading the full blurb is the whole reason for
 * hovering a description cell.
 */
function cellTitle(row: LibraryTagRow, key: string): string {
  if (key === SELECT_KEY) {
    return row.bookTitle
  }

  if (key === AUDIO_KEY) {
    return playingFileId.value === row.fileId ? `Stop ${row.fileName}` : `Play ${row.fileName}`
  }

  if (key === FILENAME_KEY) {
    if (row.error) return `${row.path ?? row.fileName}\n\n${row.error}`
    if (row.pathLocked) {
      return `${row.fileName}\n\nLocked: organizing may not rename this file or move its folder.`
    }
    if (row.fileNameMismatched && row.expectedFileName) {
      return `Now: ${row.fileName}\n\nOrganizing would rename it to: ${row.expectedFileName}`
    }
    return row.path ?? row.fileName
  }

  if (key === CHAPTERS_KEY) {
    const reason = row.chapterReason ?? 'Chapters have not been inspected.'
    if (row.chapterPlanPending) return `${reason}\n\nThe fix is being worked out in the background.`
    if (REPAIRABLE_HEALTH.has(row.chapterHealth)) {
      return row.chapterRepairable
        ? `${reason}\n\nA repair can fix this automatically.`
        : `${reason}\n\nNo automatic fix: the book needs an ASIN match or transcription on.`
    }
    return reason
  }

  if (key === AUDIT_KEY) {
    return row.audioAuditReason ?? 'Not yet listened to. Tick the row and press Listen.'
  }

  if (key === PATH_KEY) {
    const here = cellText(row, key)
    if (row.pathLocked) {
      return `Locked: this book's folder stays put.\n\nHere: ${here}`
    }

    if (row.pathMismatched && row.expectedPath) {
      return `Now: ${here}\n\nOrganizing would move it to: ${row.expectedPath}`
    }

    // Silence rather than reassurance when organizing could not answer for this book:
    // "already correct" is a claim, and nothing here is in a position to make it.
    return row.expectedPath ? here : `${here}\n\nWhere this belongs could not be worked out.`
  }

  const current = row.tags[key] ?? ''
  const expected = row.expected[key] ?? ''

  if (isLocked(row, key)) {
    return `Locked: no write may touch this tag on this file.\n\nNow: ${current || '(empty)'}`
  }

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
    result = result.filter((row) => needsWork(row))
  }

  if (chapterFilter.value === 'issues') {
    result = result.filter(hasChapterIssue)
  } else if (chapterFilter.value === 'audio-mismatch') {
    result = result.filter(hasAudioIssue)
  } else if (chapterFilter.value === 'audio-unheard') {
    result = result.filter((row) => !row.audioAudit)
  } else if (chapterFilter.value !== 'all') {
    result = result.filter((row) => row.chapterHealth === chapterFilter.value)
  }

  if (searchTerms.value.length > 0) {
    result = result.filter((row) => {
      const haystack = [
        row.fileName,
        row.bookTitle,
        row.displayPath ?? row.path ?? '',
        ...Object.values(row.tags),
      ]
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

/** A tag a write would change, a file in the wrong place, or a file that would not read. */
const needsWork = (row: LibraryTagRow) =>
  hasUnlockedMismatch(row) ||
  ((row.pathMismatched || row.fileNameMismatched) && !row.pathLocked) ||
  !!row.error

const hasUnlockedMismatch = (row: LibraryTagRow) =>
  row.mismatched.some((tag) => !row.lockedTags.includes(tag))

const mismatchCount = computed(() => rows.value.filter(hasUnlockedMismatch).length)

const organizeCount = computed(
  () =>
    rows.value.filter((row) => (row.pathMismatched || row.fileNameMismatched) && !row.pathLocked)
      .length,
)

const chapterIssueCount = computed(() => rows.value.filter(hasChapterIssue).length)

const firstVisibleIndex = computed(() =>
  Math.max(0, Math.floor(scrollTop.value / rowHeight.value) - OVERSCAN),
)

const lastVisibleIndex = computed(() =>
  Math.min(
    visibleRows.value.length,
    Math.ceil((scrollTop.value + viewportHeight.value) / rowHeight.value) + OVERSCAN,
  ),
)

const windowedRows = computed(() =>
  visibleRows.value.slice(firstVisibleIndex.value, lastVisibleIndex.value),
)

const topPadding = computed(() => firstVisibleIndex.value * rowHeight.value)
const bottomPadding = computed(
  () => Math.max(0, visibleRows.value.length - lastVisibleIndex.value) * rowHeight.value,
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

/* -- Applying to the selection ------------------------------------------------ */

const applyLabel = computed(() => {
  const count = selectedFiles.value.size > 0 ? ` (${selectedFiles.value.size})` : ''
  if (applyTags.value && applyPaths.value) return `Apply tags + paths${count}`
  if (applyTags.value) return `Apply tags${count}`
  if (applyPaths.value) return `Apply paths${count}`
  return `Apply${count}`
})

const canApply = computed(
  () => selectedFiles.value.size > 0 && (applyTags.value || applyPaths.value) && !working.value,
)

const applyHint = computed(() => {
  if (selectedFiles.value.size === 0) return 'Tick some files first'
  if (!applyTags.value && !applyPaths.value) return 'Choose what Apply covers'
  const parts = [
    applyTags.value ? 'write every unlocked tag into their files' : null,
    applyPaths.value ? 'move and rename them to match the naming pattern' : null,
  ].filter(Boolean)
  return `For the selected books: ${parts.join(', and ')}.`
})

/**
 * Run whichever parts are in scope.
 *
 * Paths go first when both are. A tag write records the path it is rewriting before it
 * touches anything, so moving the file out from under a queued job is exactly how that
 * job comes back as "file is missing" — and tags written after the move land in the file
 * where it now lives.
 */
async function applySelection() {
  if (!canApply.value) return
  applyOpen.value = false

  if (applyPaths.value) {
    if (!(await organizeSelected())) return
  }

  if (applyTags.value) {
    await writeSelected()
  }

  // The ticks were the request; it has been made. Left in place they would ride along
  // into the next apply, and once the rows drop out of "Needs work" there would be no
  // way to see them, let alone clear them.
  selectedFiles.value = new Set()
}

/**
 * Organize the selected books straight from the table.
 *
 * No preview step: the table already shows every path an organize would change, cell by
 * cell, and the operator has just read it. The preview call still runs, because the
 * server's rename needs the operations it produces — it is not shown. Only when the
 * organize is refused does the modal open, since that is where an interrupted organize
 * can be repaired.
 */
async function organizeSelected(): Promise<boolean> {
  working.value = true
  actionMessage.value = 'Organizing…'
  const bookIds = [...selectedBookIds.value]
  organizingBookIds.value = new Set(bookIds)

  try {
    const previews = await apiService.previewRename([...selectedBookIds.value])
    const operations: RenameOperation[] = previews
      .filter((preview) => preview.hasChanges)
      .map((preview) => ({
        audiobookId: preview.audiobookId,
        currentFolderPath: preview.currentFolderPath,
        currentFolderSemantics: preview.currentFolderSemantics,
        newFolderPath: preview.folderChanged ? preview.newFolderPath : undefined,
        fileRenames: preview.fileRenames
          .filter((entry) => entry.changed)
          .map((entry) => ({
            fileId: entry.fileId,
            currentPath: entry.currentPath || '',
            newPath: entry.newPath || '',
          })),
      }))

    if (operations.length === 0) {
      actionMessage.value =
        'Nothing to organize: the selected files are already where the pattern puts them.'
      return true
    }

    const results = await apiService.executeRename(operations)
    const failed = results.filter((result) => !result.success)
    await refreshBooks(bookIds)

    if (failed.length > 0) {
      actionMessage.value = `Organized ${results.length - failed.length} of ${results.length} book(s). ${failed.length} failed: ${failed[0].error ?? 'unknown error'}`
      organizeOpen.value = true
      return false
    }

    actionMessage.value = `Organized ${results.length} book${results.length === 1 ? '' : 's'}.`
    return true
  } catch (err) {
    logger.warn('Organize from the tag table failed', err)
    actionMessage.value = `Organize failed: ${describe(err)}`
    organizeOpen.value = true
    return false
  } finally {
    organizingBookIds.value = new Set()
    working.value = false
  }
}

/* -- Playing a file ---------------------------------------------------------- */

/**
 * Start this row, or stop it if it is the one already playing.
 *
 * Switching rows reuses the element rather than making another, so starting a second
 * file is what stops the first — there is no state in which two are audible.
 */
async function togglePlay(row: LibraryTagRow) {
  const element = audioEl.value
  if (!element) return

  if (playingFileId.value === row.fileId) {
    element.pause()
    playingFileId.value = null
    return
  }

  actionMessage.value = null
  element.src = apiService.buildLibraryFileAudioUrl(row.fileId)
  playingFileId.value = row.fileId

  try {
    await element.play()
  } catch (err) {
    // A browser that cannot decode the container says so here rather than by staying
    // silent, which is the difference between "this file is broken" and "nothing
    // happened when I clicked".
    logger.warn(`Could not play file ${row.fileId}`, err)
    playingFileId.value = null
    actionMessage.value = `That file could not be played: ${describe(err)}`
  }
}

function onPlaybackEnded() {
  playingFileId.value = null
}

function onPlaybackError() {
  if (playingFileId.value != null) {
    actionMessage.value = 'That file could not be played in this browser.'
    playingFileId.value = null
  }
}

/* -- Selection, which is by file --------------------------------------------- */

const visibleFileIds = computed(() => new Set(visibleRows.value.map((row) => row.fileId)))

// A tick on a row the operator cannot see is a request they cannot review. When a row
// leaves the table - written and no longer needing work, or filtered out - its tick
// goes with it; the selected count only ever describes rows on screen.
watch(visibleFileIds, (visible) => {
  if (![...selectedFiles.value].some((fileId) => !visible.has(fileId))) return
  selectedFiles.value = new Set([...selectedFiles.value].filter((fileId) => visible.has(fileId)))
})

const allVisibleSelected = computed(
  () =>
    visibleFileIds.value.size > 0 &&
    [...visibleFileIds.value].every((id) => selectedFiles.value.has(id)),
)

const someVisibleSelected = computed(() =>
  [...visibleFileIds.value].some((id) => selectedFiles.value.has(id)),
)

/** The ticked rows a chapter repair has something to say about: a broken atom, CD tracks, or placeholder titles. */
const REPAIRABLE_HEALTH: ReadonlySet<ChapterHealth> = new Set([
  'corrupt',
  'oversegmented',
  'generic-titles',
])

const repairableSelection = computed(() =>
  rows.value.filter(
    (row) => selectedFiles.value.has(row.fileId) && REPAIRABLE_HEALTH.has(row.chapterHealth),
  ),
)

const repairOpen = ref(false)

const repairScopes = computed<ChapterRepairScope[]>(() => {
  const byBook = new Map<number, ChapterRepairScope>()
  for (const row of repairableSelection.value) {
    const scope = byBook.get(row.audiobookId)
    if (scope) scope.fileIds.push(row.fileId)
    else
      byBook.set(row.audiobookId, {
        audiobookId: row.audiobookId,
        title: row.bookTitle,
        fileIds: [row.fileId],
      })
  }
  return [...byBook.values()]
})

async function repairSelected(books: { audiobookId: number; fileIds: number[] }[]) {
  repairOpen.value = false
  working.value = true
  actionMessage.value = null

  let queuedFiles = 0
  const refusals: string[] = []
  for (const book of books) {
    try {
      const response = await tagJobsStore.repairChapters(book.audiobookId, book.fileIds)
      if (response.queued) queuedFiles += book.fileIds.length
      else if (response.reason) refusals.push(response.reason)
    } catch (err) {
      logger.warn(`Failed to queue a chapter repair for audiobook ${book.audiobookId}`, err)
      refusals.push(describe(err))
    }
  }

  await tagJobsStore.refresh()
  working.value = false
  selectedFiles.value = new Set()
  actionMessage.value = refusals.length
    ? `Queued ${queuedFiles} file(s) for chapter repair. ${refusals.length} refused: ${refusals[0]}`
    : `Queued ${queuedFiles} file${queuedFiles === 1 ? '' : 's'} for chapter repair.`
}

async function auditSelected() {
  working.value = true
  actionMessage.value = null

  let queued = 0
  const refusals: string[] = []
  for (const audiobookId of selectedBookIds.value) {
    try {
      const response = await tagJobsStore.auditAudio(audiobookId)
      if (response.queued) queued++
      else if (response.reason) refusals.push(response.reason)
    } catch (err) {
      logger.warn(`Failed to queue an audio audit for audiobook ${audiobookId}`, err)
      refusals.push(describe(err))
    }
  }

  await tagJobsStore.refresh()
  working.value = false
  selectedFiles.value = new Set()
  actionMessage.value = refusals.length
    ? `Listening to ${queued} book(s). ${refusals.length} refused: ${refusals[0]}`
    : `Listening to ${queued} book${queued === 1 ? '' : 's'}. Verdicts appear in the Audio column.`
}

/** The books the ticked files belong to, which is what organizing takes. */
const selectedBookIds = computed(
  () =>
    new Set(
      rows.value.filter((row) => selectedFiles.value.has(row.fileId)).map((row) => row.audiobookId),
    ),
)

/**
 * Ticking from anywhere in the selection cell, not just the checkbox inside it.
 *
 * A checkbox is thirteen pixels in a thirty-pixel row, and every near miss used to fall
 * through to the row and navigate away — losing the selection being built. The cell is
 * the target; the checkbox is only what it looks like.
 */
function selectFromCell(event: MouseEvent, row: LibraryTagRow) {
  event.stopPropagation()

  // The checkbox handles its own click, so only the space around it arrives here.
  if ((event.target as HTMLElement)?.tagName !== 'INPUT') {
    tickFrom(event, row)
  }
}

/**
 * Tick one row, or every row between it and the last one ticked when shift is held.
 *
 * The range runs over the rows as they are currently shown, not as they are stored: the
 * operator is drawing a line between two things they can see, and a range that followed
 * some other order would select rows they were not looking at.
 */
// Not `.prevent` on the box: a cancelled click on a checkbox has the browser put the
// old state back after the event finishes - after Vue has already re-rendered the new
// one - so the badge said "1 file selected" while the box sat blank.
function tickFrom(event: MouseEvent, row: LibraryTagRow) {
  const selecting = !selectedFiles.value.has(row.fileId)

  if (event.shiftKey && rangeAnchorFileId.value != null) {
    selectRange(rangeAnchorFileId.value, row.fileId, selecting)
  } else {
    toggleFile(row.fileId)
  }

  // The row just clicked anchors the next range, shift or no shift — which is what makes
  // a second shift-click extend from where the last one landed.
  rangeAnchorFileId.value = row.fileId
}

function selectRange(fromFileId: number, toFileId: number, selecting: boolean) {
  const shown = visibleRows.value
  const from = shown.findIndex((candidate) => candidate.fileId === fromFileId)
  const to = shown.findIndex((candidate) => candidate.fileId === toFileId)

  // An anchor the filter or the sort has since hidden cannot bound anything, so the
  // click falls back to meaning only itself.
  if (from === -1 || to === -1) {
    toggleFile(toFileId)
    return
  }

  const [start, end] = from <= to ? [from, to] : [to, from]
  const next = new Set(selectedFiles.value)
  for (let index = start; index <= end; index++) {
    const fileId = shown[index].fileId
    if (selecting) {
      next.add(fileId)
    } else {
      next.delete(fileId)
    }
  }

  selectedFiles.value = next
  actionMessage.value = null
}

function toggleFile(fileId: number) {
  const row = rows.value.find((candidate) => candidate.fileId === fileId)
  if (row && isBusy(row)) return
  const next = new Set(selectedFiles.value)
  if (!next.delete(fileId)) {
    next.add(fileId)
  }

  selectedFiles.value = next
  actionMessage.value = null
}

/**
 * Select or clear every file the current filter shows.
 *
 * Files hidden by the filter are left exactly as they were rather than cleared: the
 * ordinary way to build a selection is to filter, tick, filter again, and a clear that
 * reached past the filter would silently undo the first half of that.
 */
function toggleAllVisible() {
  const next = new Set(selectedFiles.value)
  if (allVisibleSelected.value) {
    visibleFileIds.value.forEach((id) => next.delete(id))
  } else {
    visibleFileIds.value.forEach((id) => next.add(id))
  }

  selectedFiles.value = next
  actionMessage.value = null
}

/* -- Locks ------------------------------------------------------------------- */

/** The ticked rows, which is what a bulk lock applies to. */
const selectedRows = computed(() => rows.value.filter((row) => selectedFiles.value.has(row.fileId)))

/** A column reads as locked only when every selected file has it locked. */
function columnLocked(key: string) {
  const selected = selectedRows.value
  return selected.length > 0 && selected.every((row) => isLocked(row, key))
}

async function toggleLock(row: LibraryTagRow, key: string) {
  await applyLocks([row.fileId], key, !isLocked(row, key))
}

async function toggleColumnLock(key: string) {
  const selected = selectedRows.value
  if (selected.length === 0) return

  await applyLocks(
    selected.map((candidate) => candidate.fileId),
    key,
    !columnLocked(key),
  )
}

/**
 * Record a lock and fold the server's answer back into the table.
 *
 * The rows are updated from what came back rather than from what was asked for, so a
 * file that had been deleted underneath the table does not end up drawn as locked.
 */
async function applyLocks(fileIds: number[], key: string, locked: boolean) {
  working.value = true
  actionMessage.value = null

  try {
    if (key === FILENAME_KEY) {
      const result = await apiService.setPathLocks(fileIds, locked)
      rows.value = rows.value.map((row) =>
        String(row.fileId) in result ? { ...row, pathLocked: result[String(row.fileId)] } : row,
      )
    } else {
      const result = await apiService.setTagLocks(fileIds, [key], locked)
      rows.value = rows.value.map((row) => {
        const updated = result[String(row.fileId)]
        return updated ? { ...row, lockedTags: updated } : row
      })
    }
  } catch (err) {
    logger.error('Failed to change a lock', err)
    actionMessage.value = `That lock could not be saved: ${describe(err)}`
  } finally {
    working.value = false
  }
}

/* -- Writing and organizing the selection ------------------------------------ */

/**
 * Queue a tag write for the ticked files, one job per book.
 *
 * The ticks are per file but a job is per book, so the files are grouped and each book's
 * job carries its own list — ticking one story of a four-story collection writes that
 * story and leaves its siblings alone.
 *
 * No tag list is sent, so each file gets every tag its mapping allows minus whatever is
 * locked on it, which is the planner's decision rather than this table's and so stays
 * true of the write that runs a minute later.
 */
async function writeSelected() {
  const byBook = new Map<number, number[]>()
  for (const row of rows.value) {
    if (!selectedFiles.value.has(row.fileId)) continue
    const forBook = byBook.get(row.audiobookId)
    if (forBook) forBook.push(row.fileId)
    else byBook.set(row.audiobookId, [row.fileId])
  }

  if (byBook.size === 0) return

  working.value = true
  actionMessage.value = null

  let queuedFiles = 0
  const refusals: string[] = []

  for (const [audiobookId, fileIds] of byBook) {
    try {
      const response = await apiService.writeTags(audiobookId, undefined, undefined, fileIds)
      if (response.queued) {
        queuedFiles += fileIds.length
      } else if (response.reason) {
        refusals.push(response.reason)
      }
    } catch (err) {
      logger.warn(`Failed to queue a tag write for audiobook ${audiobookId}`, err)
      refusals.push(describe(err))
    }
  }

  // The rows go busy from the store's job list; pull it now rather than waiting for the
  // first SignalR update, so the tick and the dimming land together.
  await tagJobsStore.refresh()

  working.value = false
  actionMessage.value = refusals.length
    ? `Queued ${queuedFiles} of ${selectedFiles.value.size} file(s). ${refusals.length} refused: ${refusals[0]}`
    : `Queued ${queuedFiles} file${queuedFiles === 1 ? '' : 's'} for tag writing.`
}

/**
 * Files have moved, so every path in the table is stale — and so are the tags, because
 * the rows are keyed on paths the cache no longer knows.
 */
async function onOrganized() {
  organizeOpen.value = false
  actionMessage.value = 'Organized. Re-reading the library…'
  await load(false)
  actionMessage.value = null
}

const describe = (err: unknown) => (err instanceof Error ? err.message : String(err))

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

// A server that predates locks — a rolled-back image, say — answers without these
// fields, and every lock check in the table runs per cell.
const normalizeRow = (row: LibraryTagRow): LibraryTagRow => ({
  ...row,
  lockedTags: row.lockedTags ?? [],
  pathMismatched: row.pathMismatched ?? false,
  pathLocked: row.pathLocked ?? false,
  fileNameMismatched: row.fileNameMismatched ?? false,
})

async function load(refresh: boolean) {
  loading.value = rows.value.length === 0
  refreshing.value = true
  error.value = null

  try {
    const table = await apiService.getLibraryTags(refresh)
    columns.value = table.columns

    // Normalised once here rather than guarded at every use: a server that predates
    // locks — a rolled-back image, say — answers without the field, and every lock check
    // in the table runs per cell.
    rows.value = table.rows.map(normalizeRow)

    // A stored column list can name a tag the catalog no longer has. Dropping it here
    // beats rendering a column of permanent blanks. With nothing remembered — or nothing
    // left after the drop — the table opens on every tag.
    const known = new Set(table.columns.map((column) => column.tag))
    const kept = visibleTags.value.filter((tag) => known.has(tag))
    visibleTags.value = kept.length > 0 || columnsChosen.value ? kept : allColumns()
    lastTable = { columns: columns.value, rows: rows.value }
  } catch (err) {
    logger.error('Failed to load the library tag table', err)
    error.value = err instanceof Error ? err.message : String(err)
    // A refresh that fails over a table already on screen is a notice, not a blank page.
    if (rows.value.length > 0) actionMessage.value = `Could not re-read the library: ${error.value}`
  } finally {
    loading.value = false
    refreshing.value = false
    await nextTick()
    measureViewport()
  }
}

function openBook(row: LibraryTagRow) {
  if (isBusy(row)) return
  router.push({ name: 'audiobook-detail', params: { id: row.audiobookId }, query: { tab: 'tags' } })
}

function onDocumentClick(event: MouseEvent) {
  const target = event.target as Node
  if (columnsOpen.value && columnsMenuEl.value && !columnsMenuEl.value.contains(target)) {
    columnsOpen.value = false
  }

  if (applyOpen.value && applyMenuEl.value && !applyMenuEl.value.contains(target)) {
    applyOpen.value = false
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

    const storedShowPath = localStorage.getItem(SHOW_PATH_KEY)
    if (storedShowPath !== null) {
      showPath.value = storedShowPath === 'true'
    }

    showProposals.value = localStorage.getItem(PROPOSALS_KEY) === 'true'
    onlyMismatched.value = localStorage.getItem(NEEDS_WORK_KEY) === 'true'

    const storedChapterFilter = localStorage.getItem(CHAPTER_FILTER_KEY)
    if (storedChapterFilter && CHAPTER_FILTERS.some((f) => f.value === storedChapterFilter)) {
      chapterFilter.value = storedChapterFilter as ChapterFilter
    }

    const storedSort = localStorage.getItem(SORT_KEY)
    if (storedSort) {
      const parsed = JSON.parse(storedSort)
      if (parsed && typeof parsed.key === 'string') {
        sort.value = { key: parsed.key, ascending: parsed.ascending !== false }
      }
    }

    const storedScope = localStorage.getItem(APPLY_SCOPE_KEY)
    if (storedScope) {
      const parsed = JSON.parse(storedScope)
      if (parsed && typeof parsed === 'object') {
        applyTags.value = parsed.tags !== false
        applyPaths.value = parsed.paths === true
      }
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

watch([applyTags, applyPaths], ([tags, paths]) => {
  try {
    localStorage.setItem(APPLY_SCOPE_KEY, JSON.stringify({ tags, paths }))
  } catch {}
})

// The filter and the sort are how the page is being worked - a session on "needs work"
// sorted by path is the same session after a reload. The search box is not remembered:
// a stale query on open hides rows for no visible reason.
watch(onlyMismatched, (value) => {
  try {
    localStorage.setItem(NEEDS_WORK_KEY, String(value))
  } catch {}
})

watch(chapterFilter, (value) => {
  try {
    localStorage.setItem(CHAPTER_FILTER_KEY, value)
  } catch {}
})

watch(
  sort,
  (value) => {
    try {
      localStorage.setItem(SORT_KEY, JSON.stringify(value))
    } catch {}
  },
  { deep: true },
)

watch(showPath, (value) => {
  try {
    localStorage.setItem(SHOW_PATH_KEY, String(value))
  } catch {}
})

/*
 * Rows change height here, so a scroll position measured in pixels now means a different
 * row. Rescaling it keeps whatever was at the top of the screen at the top of the screen,
 * which is the difference between a toggle and losing your place in four hundred files.
 */
watch(showProposals, (next, previous) => {
  try {
    localStorage.setItem(PROPOSALS_KEY, String(next))
  } catch {}

  const previousHeight = previous ? PROPOSAL_ROW_HEIGHT : ROW_HEIGHT
  const nextHeight = next ? PROPOSAL_ROW_HEIGHT : ROW_HEIGHT
  const topRow = Math.round(scrollTop.value / previousHeight)

  void nextTick(() => {
    const target = topRow * nextHeight
    scrollTop.value = target
    if (scrollEl.value) scrollEl.value.scrollTop = target
  })
})

// Lock toggles and the like replace the row array; the cross-navigation copy follows it.
watch(rows, (current) => {
  if (lastTable) lastTable = { columns: columns.value, rows: current }
})

// Scrolling back to the top on a re-filter: the window is an index range, and leaving it
// where it was would show a blank band below a shorter list.
watch([search, onlyMismatched, chapterFilter, sort], () => {
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
  audioEl.value?.pause()
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

.toolbar-select {
  height: 30px;
  padding: 0 8px;
  border: 1px solid var(--bg-tertiary);
  border-radius: var(--radius-md);
  background: var(--bg-primary);
  color: var(--text-secondary);
  font-size: 0.8rem;
  cursor: pointer;
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

.columns-menu,
.apply-menu {
  position: relative;
}

/* The caret is joined to the button so the two read as one control with a choice. */
.apply-menu {
  display: inline-flex;
}

.apply-btn {
  border-top-right-radius: 0;
  border-bottom-right-radius: 0;
}

.apply-caret {
  margin-left: -1px;
  padding: 0 6px;
  border-top-left-radius: 0;
  border-bottom-left-radius: 0;
}

.apply-dropdown {
  width: 230px;
}

/*
 * The columns menu puts its code hint in a third column, which works there because a tag
 * name is short. "moved and renamed" is not, so here the hint sits under its label
 * instead of pushing the menu open past its own width.
 */
.apply-dropdown .columns-option {
  grid-template-columns: auto 1fr;
  align-items: center;
  row-gap: 2px;
}

.apply-dropdown .columns-option code {
  grid-column: 2;
  font-size: 0.72rem;
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

.tags-th--frozen {
  z-index: 3;
}

.tags-th--select {
  left: 0;
  padding: 0;
  text-align: center;
}

.tags-th--audio {
  left: var(--select-width, 34px);
  padding: 0;
}

.tags-th--sticky {
  left: calc(var(--select-width, 34px) + var(--audio-width, 30px));
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

/* Work in flight: the row reads as pending and takes no clicks until the job ends. */
.tags-row--busy {
  cursor: progress;
}

.tags-row--busy .tags-td {
  color: var(--text-muted, #6c757d);
  font-style: italic;
}

.cell-busy {
  opacity: 1;
  cursor: progress;
}

.cell-busy::before {
  content: '';
  display: block;
  width: 10px;
  height: 10px;
  border: 2px solid var(--brand-500, #4dabf7);
  border-right-color: transparent;
  border-radius: 50%;
  animation: tags-busy-spin 0.9s linear infinite;
}

@keyframes tags-busy-spin {
  to {
    transform: rotate(360deg);
  }
}

.tags-row:hover .tags-td {
  background: var(--bg-tertiary);
}

.tags-row:focus-visible {
  outline: 2px solid var(--brand-focus);
  outline-offset: -2px;
}

.tags-td {
  height: var(--row-height, 30px);
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

.tags-td--frozen {
  position: sticky;
  z-index: 1;
}

.tags-td--select {
  left: 0;
  padding: 0;
  text-align: center;
  cursor: pointer;
  user-select: none;
}

.tags-td--audio {
  left: var(--select-width, 34px);
  padding: 0;
  text-align: center;
}

.tags-td--sticky {
  left: calc(var(--select-width, 34px) + var(--audio-width, 30px));
  color: var(--text-primary);
}

.row-play {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  padding: 0;
  border: none;
  border-radius: var(--radius-sm);
  background: none;
  color: var(--text-muted);
  cursor: pointer;
  opacity: 0.5;
  transition: var(--transition-fast);
}

.play-icon {
  width: 14px;
  height: 14px;
}

.tags-row:hover .row-play,
.row-play:focus-visible {
  opacity: 1;
}

.row-play:hover {
  opacity: 1;
  color: var(--text-primary);
}

/* The one playing stays lit while the pointer is anywhere else in the table. */
.row-play--on {
  opacity: 1;
  color: var(--brand-400);
}

/* A frozen column has to repaint its own stripe: the row's background sits behind it. */
.tags-row--striped .tags-td--frozen {
  background: var(--bg-secondary);
}

.tags-row:hover .tags-td--frozen {
  background: var(--bg-tertiary);
}

.row-select {
  cursor: pointer;
  margin: 0;
  vertical-align: middle;
}

.cell {
  display: flex;
  align-items: center;
  gap: 4px;
  min-width: 0;
  height: 100%;
}

/*
 * The value and its proposal stack, while the padlock stays beside the pair: it applies
 * to both lines, and a lock that sat on the first would read as belonging only to it.
 */
.cell-lines {
  display: flex;
  flex-direction: column;
  justify-content: center;
  flex: 1;
  min-width: 0;
}

/*
 * Both lines are the same font at the same size on the same line grid, which is what
 * makes the pair readable as a diff: identical prefixes then occupy identical width, so
 * the first character that differs is the first character out of column. A smaller
 * proposal saved a few pixels and cost exactly the comparison the second line is for.
 */
.cell-text,
.cell-proposal {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  line-height: 20px;
}

.cell-proposal {
  color: var(--warning-500);
}

/*
 * Top-aligned once a second line exists, so the values a reader is scanning down stay on
 * one baseline whether or not the cell beside them has a proposal under it.
 */
.tags-table--proposals .cell {
  align-items: flex-start;
  padding-top: 4px;
}

.tags-table--proposals .cell-lines {
  justify-content: flex-start;
}

.tags-table--proposals .cell-lock {
  margin-top: 2px;
}

/* Neither a checkbox nor a play button has anything underneath it, so both keep the
   whole cell to centre themselves in. */
.tags-table--proposals .row-select,
.tags-table--proposals .row-play {
  margin-top: 4px;
}

/*
 * Drawn on every tag cell, faintly. It has to be visible to be an affordance — a padlock
 * that only appears under the pointer is one nobody finds — but it is also on several
 * hundred cells at once, so it sits well below the yellow of a cell that is mistagged
 * and only comes up to full strength when it is hovered or actually holding something.
 */
.cell-lock,
.th-lock {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: none;
  width: 16px;
  height: 16px;
  padding: 0;
  border: none;
  border-radius: var(--radius-sm);
  background: none;
  color: var(--text-muted);
  cursor: pointer;
  opacity: 0.4;
  transition: var(--transition-fast);
}

.lock-icon {
  width: 13px;
  height: 13px;
}

.tags-row:hover .cell-lock,
.cell-lock:focus-visible,
.tags-th:hover .th-lock,
.th-lock:focus-visible {
  opacity: 0.75;
}

.cell-lock:hover,
.th-lock:hover:not(:disabled) {
  opacity: 1;
}

.cell-lock--on,
.th-lock--on {
  opacity: 1;
  color: var(--brand-400);
}

.cell-lock:hover,
.th-lock:hover:not(:disabled) {
  color: var(--text-primary);
}

.th-lock {
  position: absolute;
  top: 7px;
  right: 8px;
}

.th-lock:disabled {
  cursor: default;
  opacity: 0.25;
}

.tags-td--mismatch {
  color: var(--warning-500);
  box-shadow: inset 2px 0 0 var(--warning-500);
}

.tags-td--sticky.tags-td--mismatch {
  box-shadow: none;
}

/* A broken or CD-track chapter list is a defect; a missing or placeholder one is a note. */
.tags-td--chapters-issue {
  color: var(--danger-500);
  box-shadow: inset 2px 0 0 currentColor;
}

.tags-td--chapters-fixable {
  color: var(--warning-500);
  box-shadow: inset 2px 0 0 currentColor;
}

.tags-td--chapters-note {
  color: var(--text-muted);
}

/*
 * A locked cell reads as settled rather than as wrong: the value stays legible, and the
 * padlock beside it is what says why nothing will happen to it.
 */
.tags-td--locked {
  color: var(--text-muted);
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

.icon-sprite {
  position: absolute;
  width: 0;
  height: 0;
  overflow: hidden;
}

.count-badge--selected {
  background: var(--brand-500-transparent, rgba(99, 102, 241, 0.16));
  color: var(--brand-400);
}

.toolbar-message {
  flex-basis: 100%;
  margin: 0;
  color: var(--text-secondary);
  font-size: 0.78rem;
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

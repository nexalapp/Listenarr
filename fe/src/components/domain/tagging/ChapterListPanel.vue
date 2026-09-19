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
  <div class="chapter-panel">
    <div v-if="loading" class="chapter-state">
      <PhSpinner class="ph-spin" />
      <p>Reading the files' chapters…</p>
    </div>

    <div v-else-if="error" class="chapter-state chapter-state--error">
      <PhWarningCircle />
      <p>{{ error }}</p>
    </div>

    <div v-else-if="files.length === 0" class="chapter-state">
      <PhListNumbers />
      <p>
        Chapters are only inspected in M4B files. Convert this book to M4B to have its chapter
        structure checked and repaired.
      </p>
    </div>

    <template v-else>
      <section v-for="file in files" :key="file.fileId" class="chapter-file">
        <header class="chapter-file-header" :class="`chapter-file-header--${severity(file)}`">
          <div class="chapter-file-title">
            <PhFileAudio />
            <span class="chapter-file-name">{{ file.name }}</span>
          </div>
          <div class="chapter-file-verdict">
            <strong>{{ verdictLabel(file.chapterHealth) }}</strong>
            <span v-if="file.chapterReason"> — {{ file.chapterReason }}</span>
            <span v-if="file.error"> — {{ file.error }}</span>
          </div>
          <p class="chapter-file-explain">{{ explain(file) }}</p>
          <div class="chapter-file-atoms" v-if="file.neroAtom">
            <span
              class="atom-chip"
              :class="`atom-chip--${file.neroAtom}`"
              :title="
                file.neroAtomError ?? 'The Nero chapter atom (chpl), which Plex and Prologue read.'
              "
            >
              Nero atom: {{ file.neroAtom }}
            </span>
            <span
              class="atom-chip"
              :class="file.chapterTrack ? 'atom-chip--ok' : 'atom-chip--missing'"
              title="The QuickTime chapter track, which Apple players and ffmpeg read."
            >
              Chapter track: {{ file.chapterTrack ? 'present' : 'missing' }}
            </span>
            <span class="atom-chip"
              >{{ file.chapters.length }} chapters · {{ formatTime(file.durationSeconds) }}</span
            >
          </div>
          <div
            v-if="proposalFor(file.fileId)"
            class="chapter-proposal-summary"
            :class="{ 'chapter-proposal-summary--rejected': !proposalFor(file.fileId)!.repairable }"
          >
            <PhArrowRight />
            <span v-if="proposalFor(file.fileId)!.repairable">
              Proposed:
              <strong>{{ proposalFor(file.fileId)!.chapters?.length ?? 0 }} chapters</strong> from
              {{ sourceLabel(proposalFor(file.fileId)!.source) }} —
              {{ proposalFor(file.fileId)!.note }}
            </span>
            <span v-else>No fix available: {{ proposalFor(file.fileId)!.rejection }}</span>
          </div>
          <div class="chapter-file-actions">
            <button
              v-if="isRepairable(file.chapterHealth) && !proposalFor(file.fileId)"
              type="button"
              class="chapter-btn chapter-btn--quiet"
              :disabled="proposing.has(file.fileId)"
              :title="
                proposing.has(file.fileId)
                  ? 'Working it out — listening to the marks can take a minute'
                  : 'Work out what a repair would write, and show it beside each chapter'
              "
              @click="propose(file.fileId)"
            >
              {{ proposing.has(file.fileId) ? 'Working out the fix…' : 'Show proposed fix' }}
            </button>
            <button
              v-if="
                isRepairable(file.chapterHealth) &&
                (!proposalFor(file.fileId) || proposalFor(file.fileId)!.repairable)
              "
              type="button"
              class="chapter-btn"
              :disabled="disabled"
              :title="disabled ? disabledReason : 'Preview and rebuild this file’s chapters'"
              @click="emit('repair', file.fileId)"
            >
              {{ proposalFor(file.fileId) ? 'Apply this fix' : 'Repair chapters' }}
            </button>
          </div>
        </header>

        <table v-if="file.chapters.length" class="chapter-table">
          <thead>
            <tr>
              <th class="num">#</th>
              <th class="time">Start</th>
              <th class="time">Length</th>
              <th>Title</th>
              <th v-if="proposalFor(file.fileId)?.repairable" class="proposed">Proposed</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="(chapter, index) in visibleChapters(file)"
              :key="index"
              :class="{
                'chapter-row--placeholder': chapter.placeholder,
                'chapter-row--merged':
                  proposalFor(file.fileId)?.repairable && proposedFor(file, chapter) === null,
              }"
            >
              <td class="num">{{ index + 1 }}</td>
              <td class="time">{{ formatTime(chapter.startSeconds) }}</td>
              <td class="time">{{ formatTime(chapter.endSeconds - chapter.startSeconds) }}</td>
              <td class="title">
                {{ chapter.title || '(untitled)' }}
                <span
                  v-if="chapter.placeholder"
                  class="placeholder-tag"
                  title="A ripping tool's name, not the author's."
                  >placeholder</span
                >
              </td>
              <td v-if="proposalFor(file.fileId)?.repairable" class="proposed">
                <template v-if="proposedFor(file, chapter) === null">
                  <span class="merged">merged into the chapter above</span>
                </template>
                <template v-else>
                  <span
                    class="proposed-title"
                    :class="{
                      'proposed-title--same': proposedFor(file, chapter)?.title === chapter.title,
                    }"
                  >
                    {{ proposedFor(file, chapter)?.title }}
                  </span>
                  <span
                    v-if="proposedFor(file, chapter)?.heard"
                    class="heard"
                    :title="proposedFor(file, chapter)?.heard ?? undefined"
                  >
                    “{{ heardText(proposedFor(file, chapter)!.heard!) }}”
                  </span>
                </template>
              </td>
            </tr>
            <tr
              v-for="(extra, index) in unmatchedProposed(file)"
              :key="`extra-${index}`"
              class="chapter-row--added"
            >
              <td class="num">+</td>
              <td class="time">{{ formatTime(extra.startSeconds) }}</td>
              <td class="time">{{ formatTime(extra.endSeconds - extra.startSeconds) }}</td>
              <td class="title"><span class="merged">no mark here today</span></td>
              <td class="proposed">
                <span class="proposed-title">{{ extra.title }}</span>
              </td>
            </tr>
          </tbody>
        </table>
        <button
          v-if="file.chapters.length > COLLAPSED_ROWS && !expanded.has(file.fileId)"
          type="button"
          class="chapter-more"
          @click="expand(file.fileId)"
        >
          Show all {{ file.chapters.length }} chapters
        </button>
      </section>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import {
  PhArrowRight,
  PhFileAudio,
  PhListNumbers,
  PhSpinner,
  PhWarningCircle,
} from '@phosphor-icons/vue'
import { apiService } from '@/services/api'
import { logger } from '@/utils/logger'
import type {
  BookChapter,
  BookChapterFile,
  ChapterHealth,
  ChapterRepairChapter,
  ChapterRepairFile,
} from '@/types'

const props = defineProps<{
  audiobookId: number | null
  disabled?: boolean
  disabledReason?: string
}>()

const emit = defineEmits<{
  (event: 'repair', fileId: number): void
}>()

const COLLAPSED_ROWS = 30

const loading = ref(false)
const error = ref<string | null>(null)
const files = ref<BookChapterFile[]>([])
const expanded = ref(new Set<number>())

const REPAIRABLE: ReadonlySet<ChapterHealth> = new Set([
  'corrupt',
  'oversegmented',
  'generic-titles',
])
const isRepairable = (health: ChapterHealth) => REPAIRABLE.has(health)

function verdictLabel(health: ChapterHealth) {
  switch (health) {
    case 'healthy':
      return 'Chapters look right'
    case 'corrupt':
      return 'Corrupt chapter atom'
    case 'oversegmented':
      return 'Chapters are CD tracks'
    case 'generic-titles':
      return 'Placeholder titles'
    case 'none':
      return 'No chapter marks'
    default:
      return 'Not inspected'
  }
}

/** Amber when a repair can fix it automatically, red when it cannot; the proposal, once fetched, is definitive. */
function severity(file: BookChapterFile) {
  switch (file.chapterHealth) {
    case 'corrupt':
    case 'oversegmented':
    case 'generic-titles': {
      const proposal = proposals.value.get(file.fileId)
      const fixable = proposal ? proposal.repairable : file.chapterRepairable
      return fixable ? 'fixable' : 'issue'
    }
    case 'none':
      return 'note'
    case 'healthy':
      return 'ok'
    default:
      return 'none'
  }
}

/** The verdict in a sentence a person can act on. */
function explain(file: BookChapterFile) {
  switch (file.chapterHealth) {
    case 'healthy':
      return 'The chapter atom parses and agrees with the list the file plays. Players will show these chapters.'
    case 'corrupt':
      return 'The Nero chapter atom — the one Plex and Prologue read — is damaged, so those players show no chapters or refuse the file. The chapter track usually survives, and a repair rebuilds the atom from it.'
    case 'oversegmented':
      return 'There are many short, evenly sized marks: a CD rip’s tracks, not the author’s chapters. A repair finds the real chapters from the edition’s list or by listening for the narrator’s announcements, and merges the tracks between them.'
    case 'generic-titles':
      return 'Every title is a ripping tool’s name — “Chapter 001 - 00:06:20” — rather than the author’s. A repair names them from the edition’s list or from what the narrator announces at each mark.'
    case 'none':
      return 'A long file with no chapter marks at all. Players will show it as one chapter.'
    default:
      return file.error ?? 'This file has not been inspected.'
  }
}

// ---- the proposed fix -------------------------------------------------------------

/** What a repair would write, per file, once asked for. */
const proposals = ref(new Map<number, ChapterRepairFile>())
const proposing = ref(new Set<number>())

const proposalFor = (fileId: number) => proposals.value.get(fileId) ?? null

const sourceLabel = (source?: string | null) => {
  switch (source) {
    case 'Played':
      return 'the file’s own chapter track'
    case 'Audnexus':
      return 'Audnexus (the edition’s list)'
    case 'RecoveredAtom':
      return 'the damaged atom'
    case 'Announcements':
      return 'what the narrator announced'
    default:
      return 'an unknown source'
  }
}

/** Marks within this many seconds are the same place. */
const SAME_MARK = 1.5

/**
 * The proposed chapter that starts at this mark, or null when the mark is folded into
 * the chapter before it. Undefined until a proposal has been fetched.
 */
function proposedFor(
  file: BookChapterFile,
  chapter: BookChapter,
): ChapterRepairChapter | null | undefined {
  const proposal = proposals.value.get(file.fileId)
  if (!proposal?.chapters) return undefined
  return (
    proposal.chapters.find((c) => Math.abs(c.startSeconds - chapter.startSeconds) <= SAME_MARK) ??
    null
  )
}

/** Proposed chapters that start where the file has no mark today (an edition's list can). */
function unmatchedProposed(file: BookChapterFile): ChapterRepairChapter[] {
  const proposal = proposals.value.get(file.fileId)
  if (!proposal?.repairable || !proposal.chapters) return []
  return proposal.chapters.filter(
    (c) => !file.chapters.some((mark) => Math.abs(mark.startSeconds - c.startSeconds) <= SAME_MARK),
  )
}

function heardText(heard: string) {
  const flat = heard.replace(/\s*\n\s*/g, ' · ').trim()
  return flat.length > 70 ? `${flat.slice(0, 67)}…` : flat
}

async function propose(fileId: number) {
  if (props.audiobookId == null) return
  proposing.value = new Set([...proposing.value, fileId])
  try {
    const preview = await apiService.previewChapterRepair(props.audiobookId, [fileId])
    const file = preview.files.find((f) => f.fileId === fileId)
    if (file) {
      proposals.value = new Map([...proposals.value, [fileId, file]])
      expanded.value = new Set([...expanded.value, fileId])
    }
  } catch (err) {
    logger.warn('Failed to work out a chapter fix', err)
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    const next = new Set(proposing.value)
    next.delete(fileId)
    proposing.value = next
  }
}

function visibleChapters(file: BookChapterFile): BookChapter[] {
  return expanded.value.has(file.fileId) ? file.chapters : file.chapters.slice(0, COLLAPSED_ROWS)
}

function expand(fileId: number) {
  expanded.value = new Set([...expanded.value, fileId])
}

function formatTime(seconds: number) {
  const total = Math.max(0, Math.floor(seconds))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  const mm = String(m).padStart(2, '0')
  const ss = String(s).padStart(2, '0')
  return h > 0 ? `${h}:${mm}:${ss}` : `${m}:${ss}`
}

async function load() {
  if (props.audiobookId == null) return
  loading.value = true
  error.value = null
  try {
    files.value = (await apiService.getBookChapters(props.audiobookId)).files
    // A fresh read makes every earlier proposal stale.
    proposals.value = new Map()
  } catch (err) {
    logger.warn('Failed to load chapters', err)
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

watch(
  () => props.audiobookId,
  () => void load(),
  { immediate: true },
)

defineExpose({ load })
</script>

<style scoped>
.chapter-panel {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.chapter-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.6rem;
  padding: 2rem 1rem;
  text-align: center;
  color: var(--text-secondary);
}

.chapter-state svg {
  width: 28px;
  height: 28px;
}

.chapter-state--error {
  color: #ff6b6b;
}

.chapter-file {
  border: 1px solid var(--bg-tertiary);
  border-radius: 8px;
  overflow: hidden;
  background: var(--bg-secondary);
}

.chapter-file-header {
  padding: 12px 14px;
  border-left: 3px solid transparent;
  display: grid;
  gap: 6px;
}

.chapter-file-header--issue {
  border-left-color: #e74c3c;
}

.chapter-file-header--fixable {
  border-left-color: #f39c12;
}

.chapter-file-header--note {
  border-left-color: var(--text-muted);
}

.chapter-file-header--ok {
  border-left-color: #2ecc71;
}

.chapter-file-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
}

.chapter-file-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.chapter-file-verdict {
  font-size: 13px;
  color: var(--text-primary);
}

.chapter-file-explain {
  margin: 0;
  font-size: 13px;
  color: var(--text-secondary);
  line-height: 1.5;
}

.chapter-file-atoms {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.atom-chip {
  padding: 2px 8px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.08);
  font-size: 11px;
  color: var(--text-secondary);
}

.atom-chip--ok {
  color: #2ecc71;
  border-color: rgba(46, 204, 113, 0.35);
}

.atom-chip--broken {
  color: #e74c3c;
  border-color: rgba(231, 76, 60, 0.35);
}

.atom-chip--missing {
  color: var(--text-muted);
}

.chapter-file-actions {
  display: flex;
  gap: 8px;
}

.chapter-btn {
  padding: 4px 12px;
  border: 1px solid var(--brand-500);
  border-radius: 6px;
  background: transparent;
  color: var(--brand-500);
  font-size: 12px;
  cursor: pointer;
}

.chapter-btn--quiet {
  border-color: rgba(255, 255, 255, 0.15);
  color: var(--text-secondary);
}

.chapter-proposal-summary {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 8px 10px;
  border-radius: 6px;
  background: rgba(46, 204, 113, 0.08);
  border: 1px solid rgba(46, 204, 113, 0.25);
  font-size: 13px;
  color: var(--text-primary);
}

.chapter-proposal-summary svg {
  flex-shrink: 0;
  margin-top: 2px;
}

.chapter-proposal-summary--rejected {
  background: rgba(231, 76, 60, 0.08);
  border-color: rgba(231, 76, 60, 0.25);
}

.chapter-table .proposed {
  width: 40%;
}

.proposed-title {
  color: #2ecc71;
}

.proposed-title--same {
  color: var(--text-secondary);
}

.chapter-row--merged td {
  color: var(--text-muted);
}

.chapter-row--merged .merged,
.chapter-row--added .merged {
  font-style: italic;
  color: var(--text-muted);
  font-size: 12px;
}

.chapter-row--added td {
  background: rgba(46, 204, 113, 0.05);
}

.heard {
  display: block;
  margin-top: 2px;
  color: var(--text-muted);
  font-style: italic;
  font-size: 11px;
}

.chapter-btn:disabled {
  opacity: 0.5;
  cursor: default;
}

.chapter-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.chapter-table th {
  text-align: left;
  font-weight: 500;
  color: var(--text-muted);
  padding: 6px 14px;
  border-top: 1px solid var(--bg-tertiary);
  border-bottom: 1px solid var(--bg-tertiary);
}

.chapter-table td {
  padding: 5px 14px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.04);
}

.chapter-table .num {
  width: 3rem;
  color: var(--text-muted);
}

.chapter-table .time {
  width: 6rem;
  font-variant-numeric: tabular-nums;
  color: var(--text-secondary);
}

.chapter-row--placeholder .title {
  color: var(--text-muted);
}

.placeholder-tag {
  margin-left: 8px;
  padding: 1px 6px;
  border-radius: 999px;
  background: rgba(243, 156, 18, 0.12);
  border: 1px solid rgba(243, 156, 18, 0.25);
  color: #f39c12;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
}

.chapter-more {
  margin: 8px 14px 12px;
  padding: 0;
  border: none;
  background: none;
  color: var(--brand-500);
  font-size: 12px;
  cursor: pointer;
}
</style>

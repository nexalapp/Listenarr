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
  <Modal :visible="visible" size="lg" @close="handleClose">
    <template #header>
      <ModalHeader title="Repair Chapters" :icon="PhListNumbers" @close="handleClose" />
    </template>

    <template #default>
      <ModalBody compact maxHeight="72vh" class="chapter-modal-body">
        <div v-if="loading" class="chapter-state">
          <PhSpinner class="ph-spin chapter-icon" />
          <p>Working out what each file's chapters should be…</p>
        </div>

        <div v-else-if="error" class="chapter-state chapter-error">
          <PhWarningCircle class="chapter-icon" />
          <p>{{ error }}</p>
        </div>

        <div
          v-else-if="books.length > 0 && repairableCount === 0"
          class="chapter-state chapter-error"
        >
          <PhWarningCircle class="chapter-icon" />
          <p>None of these files can be repaired automatically.</p>
          <ul class="chapter-rejections">
            <li v-for="file in allFiles" :key="`${file.audiobookId}-${file.fileId}`">
              <strong>{{ file.name }}</strong> — {{ file.rejection ?? file.chapterReason }}
            </li>
          </ul>
        </div>

        <div v-else-if="books.length > 0" class="chapter-preview">
          <div class="info-section">
            <PhInfo />
            <p>
              A repair replaces the file's chapter structures with the list below and leaves the
              audio, tags and cover exactly as they are. The list comes from the file's own chapter
              track where the damaged atom left it intact, from Audnexus when the edition matches,
              from what the narrator was heard to announce at each mark, or from inside the damaged
              atom as a last resort. Where something was heard, it is shown beside the chapter so a
              misheard name can be caught before it is written.
            </p>
          </div>

          <section v-for="book in books" :key="book.audiobookId" class="chapter-book">
            <header v-if="books.length > 1" class="chapter-book-header">
              <PhBook />
              <span>{{ book.title }}</span>
            </header>

            <section
              v-for="file in book.preview.files"
              :key="file.fileId"
              class="chapter-file"
              :class="{ 'chapter-file--rejected': !file.repairable }"
            >
              <header class="chapter-file-header">
                <PhFileAudio />
                <span class="chapter-file-name">{{ file.name }}</span>
                <span
                  v-if="file.repairable"
                  class="chapter-badge"
                  :class="{ 'chapter-badge--partial': file.partial }"
                  :title="file.note ?? undefined"
                >
                  {{ file.chapters?.length ?? 0 }} chapters from {{ sourceLabel(file.source) }}
                  <template v-if="file.partial"> (partial)</template>
                </span>
              </header>

              <p v-if="!file.repairable" class="chapter-file-rejection">
                <PhWarningCircle />
                {{ file.rejection ?? file.chapterReason }}
              </p>

              <template v-else>
                <p v-if="file.partial" class="chapter-file-warning">
                  <PhWarningCircle />
                  {{ file.note }}
                </p>
                <ol class="chapter-list">
                  <li
                    v-for="(chapter, index) in visibleChapters(file)"
                    :key="index"
                    class="chapter-row"
                  >
                    <span class="chapter-time">{{ formatTime(chapter.startSeconds) }}</span>
                    <span class="chapter-title">{{ chapter.title || `Chapter ${index + 1}` }}</span>
                    <span
                      v-if="chapter.heard"
                      class="chapter-heard"
                      :title="heardText(chapter.heard)"
                    >
                      “{{ heardText(chapter.heard) }}”
                    </span>
                  </li>
                </ol>
                <button
                  v-if="(file.chapters?.length ?? 0) > COLLAPSED_ROWS && !expanded.has(file.fileId)"
                  type="button"
                  class="chapter-more"
                  @click="expand(file.fileId)"
                >
                  Show all {{ file.chapters?.length }} chapters
                </button>
              </template>
            </section>
          </section>
        </div>
      </ModalBody>
    </template>

    <template #footer>
      <ModalFooter :showCancel="false">
        <template #left>
          <button type="button" class="btn cancel-button" @click="handleClose">
            <PhX :size="16" />
            Cancel
          </button>
        </template>
        <template #default>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="loading || queueing || repairableCount === 0"
            @click="confirm"
          >
            <PhSpinner v-if="queueing" class="ph-spin" :size="16" />
            <PhListNumbers v-else :size="16" />
            Repair {{ repairableCount }} file(s)
          </button>
        </template>
      </ModalFooter>
    </template>
  </Modal>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  PhBook,
  PhFileAudio,
  PhInfo,
  PhListNumbers,
  PhSpinner,
  PhWarningCircle,
  PhX,
} from '@phosphor-icons/vue'
import { Modal, ModalBody, ModalFooter, ModalHeader } from '@/components/feedback'
import { apiService } from '@/services/api'
import { logger } from '@/utils/logger'
import type { ChapterRepairChapter, ChapterRepairFile, ChapterRepairPreview } from '@/types'

/**
 * One book's worth of files to consider. The Tags view sends several (one per ticked
 * book); the book page sends one. `fileIds` empty means every corrupt file of the book.
 */
export interface ChapterRepairScope {
  audiobookId: number
  title: string
  fileIds: number[]
}

const props = defineProps<{
  visible: boolean
  scopes: ChapterRepairScope[]
}>()

const emit = defineEmits<{
  (event: 'close'): void
  /** The operator approved; the parent queues one job per book. */
  (event: 'confirm', payload: { audiobookId: number; fileIds: number[] }[]): void
}>()

const COLLAPSED_ROWS = 12

const loading = ref(false)
const queueing = ref(false)
const error = ref<string | null>(null)
const books = ref<{ audiobookId: number; title: string; preview: ChapterRepairPreview }[]>([])
const expanded = ref(new Set<number>())

const allFiles = computed(() =>
  books.value.flatMap((book) =>
    book.preview.files.map((file) => ({ ...file, audiobookId: book.audiobookId })),
  ),
)

const repairableCount = computed(() => allFiles.value.filter((file) => file.repairable).length)

const sourceLabel = (source?: string | null) => {
  switch (source) {
    case 'Played':
      return 'the file’s chapter track'
    case 'Audnexus':
      return 'Audnexus'
    case 'RecoveredAtom':
      return 'the damaged atom'
    case 'Announcements':
      return 'what the narrator announced'
    default:
      return 'an unknown source'
  }
}

function visibleChapters(file: ChapterRepairFile): ChapterRepairChapter[] {
  const chapters = file.chapters ?? []
  return expanded.value.has(file.fileId) ? chapters : chapters.slice(0, COLLAPSED_ROWS)
}

function expand(fileId: number) {
  expanded.value = new Set([...expanded.value, fileId])
}

/** The heard segments on one line, trimmed to what fits a row. */
function heardText(heard: string) {
  const flat = heard.replace(/\s*\n\s*/g, ' · ').trim()
  return flat.length > 90 ? `${flat.slice(0, 87)}…` : flat
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
  loading.value = true
  error.value = null
  books.value = []
  expanded.value = new Set()

  try {
    const loaded = []
    for (const scope of props.scopes) {
      const preview = await apiService.previewChapterRepair(scope.audiobookId, scope.fileIds)
      loaded.push({ audiobookId: scope.audiobookId, title: scope.title, preview })
    }
    books.value = loaded
  } catch (err) {
    logger.warn('Failed to preview chapter repair', err)
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

function handleClose() {
  emit('close')
}

function confirm() {
  if (repairableCount.value === 0) return
  queueing.value = true
  try {
    emit(
      'confirm',
      books.value
        .map((book) => ({
          audiobookId: book.audiobookId,
          fileIds: book.preview.files.filter((file) => file.repairable).map((file) => file.fileId),
        }))
        .filter((book) => book.fileIds.length > 0),
    )
  } finally {
    queueing.value = false
  }
}

watch(
  () => [props.visible, props.scopes] as const,
  ([visible]) => {
    if (visible && props.scopes.length > 0) {
      void load()
    }
  },
  { immediate: true },
)
</script>

<style scoped>
.chapter-modal-body {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.chapter-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  padding: 2.5rem 1rem;
  text-align: center;
  color: var(--text-secondary, #adb5bd);
}

.chapter-icon {
  width: 32px;
  height: 32px;
}

.chapter-error {
  color: #ff6b6b;
}

.chapter-rejections {
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: 0.85rem;
  text-align: left;
  color: var(--text-secondary, #adb5bd);
}

.info-section {
  display: flex;
  align-items: flex-start;
  gap: 0.625rem;
  padding: 0.875rem;
  border-radius: 6px;
  background-color: rgba(77, 171, 247, 0.1);
  border: 1px solid rgba(77, 171, 247, 0.25);
  color: var(--text-secondary, #adb5bd);
  font-size: 0.875rem;
  line-height: 1.5;
}

.info-section svg {
  flex-shrink: 0;
  margin-top: 0.1rem;
  width: 18px;
  height: 18px;
}

.chapter-book + .chapter-book {
  margin-top: 1.25rem;
}

.chapter-book-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.95rem;
  font-weight: 600;
  margin-bottom: 0.5rem;
  color: var(--text-primary, #f8f9fa);
}

.chapter-file + .chapter-file {
  margin-top: 0.75rem;
}

.chapter-file--rejected {
  opacity: 0.75;
}

.chapter-file-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.875rem;
  font-weight: 600;
  margin-bottom: 0.4rem;
  color: var(--text-primary, #f8f9fa);
}

.chapter-file-name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.chapter-badge {
  padding: 2px 8px;
  border-radius: var(--radius-full);
  background: rgba(81, 207, 102, 0.16);
  color: #51cf66;
  font-size: 0.75rem;
  font-weight: 500;
  white-space: nowrap;
}

.chapter-badge--partial {
  background: rgba(255, 165, 0, 0.16);
  color: var(--warning-500, #ffa500);
}

.chapter-file-rejection,
.chapter-file-warning {
  display: flex;
  align-items: flex-start;
  gap: 0.4rem;
  margin: 0 0 0.4rem;
  font-size: 0.85rem;
}

.chapter-file-rejection {
  color: #ff6b6b;
}

.chapter-file-warning {
  color: var(--warning-500, #ffa500);
}

.chapter-file-rejection svg,
.chapter-file-warning svg {
  flex-shrink: 0;
  margin-top: 0.15rem;
}

.chapter-list {
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: 0.85rem;
  border: 1px solid var(--bg-tertiary);
  border-radius: 6px;
  overflow: hidden;
}

.chapter-row {
  display: flex;
  gap: 0.75rem;
  padding: 0.3rem 0.6rem;
}

.chapter-row:nth-child(odd) {
  background: rgba(255, 255, 255, 0.03);
}

.chapter-time {
  flex: 0 0 5.5rem;
  font-variant-numeric: tabular-nums;
  color: var(--text-muted);
}

.chapter-title {
  flex: 0 1 auto;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--text-primary, #f8f9fa);
}

.chapter-heard {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--text-muted);
  font-style: italic;
  font-size: 0.8rem;
}

.chapter-more {
  margin-top: 0.4rem;
  padding: 0;
  border: none;
  background: none;
  color: var(--brand-500);
  font-size: 0.8rem;
  cursor: pointer;
}
</style>

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
  <div class="tags-panel">
    <div v-if="loading" class="panel-state">
      <PhSpinner class="ph-spin panel-icon" />
      <p>Reading the tags in this book's files…</p>
    </div>

    <div v-else-if="error" class="panel-state panel-state--error">
      <PhWarningCircle class="panel-icon" />
      <p>{{ error }}</p>
      <button type="button" class="btn btn-secondary" @click="load">Try again</button>
    </div>

    <template v-else-if="preview">
      <div class="panel-toolbar">
        <div class="panel-summary">
          <span v-if="!preview.canWrite" class="pill pill--muted">
            <PhWarningCircle :size="13" />
            {{ preview.reason ?? 'Tags cannot be written for this book.' }}
          </span>
          <span v-else-if="writableTags.length === 0" class="pill pill--ok">
            <PhCheckCircle :size="13" />
            Every tag already matches what Listenarr would write.
          </span>
          <span v-else class="pill pill--warn">
            <PhTag :size="13" />
            {{ writableTags.length }} tag{{ writableTags.length === 1 ? '' : 's' }} would change
          </span>
          <span class="pill pill--muted"> {{ selectedCount }} selected </span>
        </div>

        <div class="panel-actions">
          <label class="panel-toggle">
            <input type="checkbox" v-model="showUnchanged" />
            <span>Show tags that already match</span>
          </label>
          <button type="button" class="btn btn-secondary btn-sm" @click="selectAll">
            Select all changes
          </button>
          <button type="button" class="btn btn-secondary btn-sm" @click="clearSelection">
            Clear
          </button>
          <button type="button" class="btn btn-secondary btn-sm" :disabled="loading" @click="load">
            <PhArrowsClockwise :size="14" />
            Re-read
          </button>
          <button
            type="button"
            class="btn btn-primary btn-sm"
            :disabled="disabled || selectedCount === 0"
            :title="writeTitle"
            @click="write"
          >
            <PhTag :size="14" />
            Write {{ selectedCount }} tag{{ selectedCount === 1 ? '' : 's' }}
          </button>
        </div>
      </div>

      <p class="panel-hint">
        The middle column is what each file carries right now. Correct anything in the right-hand
        column before writing it — an edit applies to this write only and does not change the
        mapping in Settings.
      </p>

      <section v-for="file in preview.files" :key="file.fileId" class="tags-file">
        <header class="tags-file-header">
          <PhFileAudio :size="15" />
          <span class="tags-file-name">{{ file.name }}</span>
        </header>

        <p v-if="file.error" class="tags-file-error">
          <PhWarningCircle :size="14" />
          {{ file.error }}
        </p>

        <table v-else class="tags-file-table">
          <thead>
            <tr>
              <th class="col-check"><span class="sr-only">Write</span></th>
              <th class="col-tag">Tag</th>
              <th class="col-current">In the file now</th>
              <th class="col-proposed">Value to write</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="change in visibleChanges(file)"
              :key="change.tag"
              :class="{
                'row--changed': change.action === 'Write',
                'row--inactive': !isWritable(change),
              }"
            >
              <td class="col-check">
                <input
                  type="checkbox"
                  :disabled="!isWritable(change)"
                  :checked="selected.has(change.tag)"
                  :aria-label="`Write ${change.label}`"
                  @change="toggle(change.tag)"
                />
              </td>
              <td class="col-tag">
                <span class="tag-label">{{ change.label }}</span>
                <code class="tag-key">{{ change.tag }}</code>
                <span class="tag-reason">{{ reasonFor(change) }}</span>
              </td>
              <td class="col-current">
                <span class="current-value" :title="change.current || ''">
                  {{ change.current || '—' }}
                </span>
              </td>
              <td class="col-proposed">
                <textarea
                  v-if="change.isLongText"
                  class="value-input value-input--long"
                  rows="4"
                  :disabled="change.action === 'NotConfigured'"
                  :value="valueFor(change)"
                  :aria-label="`${change.label} value to write`"
                  @input="edit(change, ($event.target as HTMLTextAreaElement).value)"
                ></textarea>
                <input
                  v-else
                  type="text"
                  class="value-input"
                  :disabled="change.action === 'NotConfigured'"
                  :value="valueFor(change)"
                  :aria-label="`${change.label} value to write`"
                  @input="edit(change, ($event.target as HTMLInputElement).value)"
                />
                <button
                  v-if="isEdited(change)"
                  type="button"
                  class="value-revert"
                  @click="revert(change)"
                >
                  <PhArrowCounterClockwise :size="12" />
                  Undo edit
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </section>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  PhArrowCounterClockwise,
  PhArrowsClockwise,
  PhCheckCircle,
  PhFileAudio,
  PhSpinner,
  PhTag,
  PhWarningCircle,
} from '@phosphor-icons/vue'
import { apiService } from '@/services/api'
import { logger } from '@/utils/logger'
import type { TagChangePreview, TagPreview, TagPreviewFile } from '@/types'

const props = defineProps<{
  audiobookId: number | null
  /** A write is already queued or running for this book, so a second one is refused anyway. */
  disabled?: boolean
  disabledReason?: string
}>()

const emit = defineEmits<{
  (event: 'write', payload: { tags: string[]; values: Record<string, string> }): void
}>()

const loading = ref(false)
const error = ref<string | null>(null)
const preview = ref<TagPreview | null>(null)
const selected = ref(new Set<string>())

// Opening on every tag, not only the ones that would change: this tab exists to check
// what the files say, and a view that hid the correct values could not be used for that.
const showUnchanged = ref(true)

/**
 * What each tag will be written as. Seeded from the server's proposal and then owned by
 * the operator — the same one-off override the preview modal offers, kept one-off so a
 * correction to a single book never quietly edits the mapping every book shares.
 */
const values = ref<Record<string, string>>({})

const proposedFor = (change: TagChangePreview) => change.proposed ?? ''

const valueFor = (change: TagChangePreview) => values.value[change.tag] ?? proposedFor(change)

const isEdited = (change: TagChangePreview) =>
  change.tag in values.value && values.value[change.tag] !== proposedFor(change)

const isWritable = (change: TagChangePreview) => {
  if (change.action === 'NotConfigured') return false
  if (change.action === 'Write') return true
  return isEdited(change) && valueFor(change).trim().length > 0
}

const reasonFor = (change: TagChangePreview) =>
  isEdited(change) ? 'Will be written as edited.' : change.reason

const writableTags = computed(() => {
  const tags = new Set<string>()
  for (const file of preview.value?.files ?? []) {
    for (const change of file.changes) {
      if (isWritable(change)) tags.add(change.tag)
    }
  }
  return Array.from(tags)
})

const selectedCount = computed(() => selected.value.size)

const writeTitle = computed(() => {
  if (props.disabled) return props.disabledReason ?? 'A tag write is already queued for this book'
  return "Write the selected tags into this book's M4B files"
})

const visibleChanges = (file: TagPreviewFile) =>
  showUnchanged.value ? file.changes : file.changes.filter(isWritable)

function edit(change: TagChangePreview, value: string) {
  values.value = { ...values.value, [change.tag]: value }

  // Typing into a row the preview would have skipped is how an operator says they want
  // it written; making them tick it as well would be a second, pointless step.
  const next = new Set(selected.value)
  if (isWritable(change)) {
    next.add(change.tag)
  } else {
    next.delete(change.tag)
  }
  selected.value = next
}

function revert(change: TagChangePreview) {
  const next = { ...values.value }
  delete next[change.tag]
  values.value = next

  const selection = new Set(selected.value)
  if (isWritable(change)) {
    selection.add(change.tag)
  } else {
    selection.delete(change.tag)
  }
  selected.value = selection
}

function toggle(tag: string) {
  const next = new Set(selected.value)
  if (next.has(tag)) {
    next.delete(tag)
  } else {
    next.add(tag)
  }
  selected.value = next
}

function selectAll() {
  selected.value = new Set(writableTags.value)
}

function clearSelection() {
  selected.value = new Set()
}

async function load() {
  if (props.audiobookId == null) return

  loading.value = true
  error.value = null

  try {
    const result = await apiService.previewTags(props.audiobookId)
    preview.value = result
    values.value = {}

    // Nothing is pre-selected. Writing replaces a library file, and a screen that arrived
    // with every change already ticked would make that one careless click away.
    selected.value = new Set()
  } catch (err) {
    logger.error('Failed to read this book’s tags', err)
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

function write() {
  if (selected.value.size === 0) return

  const tags = Array.from(selected.value)

  // The values the operator actually saw, edited or not, rather than only the tag names.
  // The write is then the diff they approved instead of whatever the patterns happen to
  // render by the time the worker gets to it.
  const chosen: Record<string, string> = {}
  for (const file of preview.value?.files ?? []) {
    for (const change of file.changes) {
      if (selected.value.has(change.tag)) chosen[change.tag] = valueFor(change)
    }
  }

  emit('write', { tags, values: chosen })
}

watch(
  () => props.audiobookId,
  (id) => {
    if (id != null) void load()
  },
  { immediate: true },
)

defineExpose({ load })
</script>

<style scoped>
.tags-panel {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.panel-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--spacing-sm);
  padding: var(--spacing-xl);
  color: var(--text-secondary);
  text-align: center;
}

.panel-state--error {
  color: var(--danger-500);
}

.panel-icon {
  font-size: 1.75rem;
}

.panel-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-md);
  flex-wrap: wrap;
}

.panel-summary,
.panel-actions {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  flex-wrap: wrap;
}

.pill {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 3px 10px;
  border-radius: var(--radius-full);
  font-size: 0.78rem;
  background: var(--bg-tertiary);
  color: var(--text-secondary);
}

.pill--warn {
  background: rgba(255, 165, 0, 0.16);
  color: var(--warning-500);
}

.pill--ok {
  background: rgba(81, 207, 102, 0.16);
  color: var(--success-500);
}

.panel-toggle {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8rem;
  color: var(--text-secondary);
  cursor: pointer;
}

.btn-sm {
  height: 30px;
  padding: 0 10px;
  font-size: 0.8rem;
  display: inline-flex;
  align-items: center;
  gap: 5px;
}

.panel-hint {
  margin: 0;
  color: var(--text-muted);
  font-size: 0.82rem;
}

.tags-file {
  border: 1px solid var(--bg-tertiary);
  border-radius: var(--radius-md);
  overflow: hidden;
  background: var(--bg-secondary);
}

.tags-file-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px var(--spacing-md);
  background: var(--bg-tertiary);
  color: var(--text-primary);
  font-size: 0.85rem;
}

.tags-file-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tags-file-error {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 0;
  padding: var(--spacing-md);
  color: var(--danger-500);
  font-size: 0.85rem;
}

.tags-file-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.82rem;
}

.tags-file-table th {
  padding: 6px var(--spacing-sm);
  text-align: left;
  font-weight: 600;
  color: var(--text-muted);
  border-bottom: 1px solid var(--bg-tertiary);
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.tags-file-table td {
  padding: 8px var(--spacing-sm);
  border-bottom: 1px solid var(--bg-tertiary);
  vertical-align: top;
}

.col-check {
  width: 34px;
  text-align: center;
}

.col-tag {
  width: 26%;
}

.col-current {
  width: 30%;
}

.row--changed .col-tag .tag-label {
  color: var(--warning-500);
}

.row--inactive {
  opacity: 0.66;
}

.tag-label {
  display: block;
  color: var(--text-primary);
  font-weight: 600;
}

.tag-key {
  display: inline-block;
  margin-top: 2px;
  color: var(--text-muted);
  font-size: 0.72rem;
}

.tag-reason {
  display: block;
  margin-top: 3px;
  color: var(--text-muted);
  font-size: 0.72rem;
}

.current-value {
  display: -webkit-box;
  -webkit-line-clamp: 4;
  line-clamp: 4;
  -webkit-box-orient: vertical;
  overflow: hidden;
  color: var(--text-secondary);
  word-break: break-word;
}

.value-input {
  width: 100%;
  padding: 5px 8px;
  border: 1px solid var(--bg-surface);
  border-radius: var(--radius-sm);
  background: var(--bg-primary);
  color: var(--text-primary);
  font-size: 0.82rem;
  font-family: inherit;
  resize: vertical;
}

.value-input:focus {
  outline: none;
  border-color: var(--brand-500);
}

.value-input:disabled {
  opacity: 0.55;
}

.value-revert {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  margin-top: 4px;
  padding: 0;
  border: none;
  background: none;
  color: var(--brand-400);
  font-size: 0.74rem;
  cursor: pointer;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

@media (max-width: 768px) {
  .tags-file-table,
  .tags-file-table tbody,
  .tags-file-table tr,
  .tags-file-table td {
    display: block;
    width: auto;
  }

  .tags-file-table thead {
    display: none;
  }

  .tags-file-table tr {
    padding: var(--spacing-sm) 0;
    border-bottom: 1px solid var(--bg-tertiary);
  }

  .tags-file-table td {
    border-bottom: none;
  }
}
</style>

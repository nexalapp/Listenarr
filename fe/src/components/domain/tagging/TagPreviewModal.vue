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
      <ModalHeader title="Write Metadata Tags" :icon="PhTag" @close="handleClose" />
    </template>

    <template #default>
      <ModalBody compact maxHeight="72vh" class="tag-modal-body">
        <div v-if="loading" class="tag-state">
          <PhSpinner class="ph-spin tag-icon" />
          <p>Reading the current tags…</p>
        </div>

        <div v-else-if="error" class="tag-state tag-error">
          <PhWarningCircle class="tag-icon" />
          <p>{{ error }}</p>
        </div>

        <div v-else-if="preview && !preview.canWrite" class="tag-state tag-error">
          <PhWarningCircle class="tag-icon" />
          <p>{{ preview.reason ?? 'Tags cannot be written for this book.' }}</p>
        </div>

        <div v-else-if="preview && !preview.hasChanges" class="tag-state tag-success">
          <PhCheckCircle class="tag-icon" />
          <p>Every tag already matches what the mapping produces. Nothing would be written.</p>
        </div>

        <div v-else-if="preview" class="tag-preview">
          <div class="info-section">
            <PhInfo />
            <p>
              Tick the tags to write, and correct any value that is wrong before writing it.
              Anything left unticked keeps the value the file already has. Both the ticks and the
              edits apply to this run only — they do not change the mapping in Settings.
            </p>
          </div>

          <div class="tag-toolbar">
            <p class="tag-toolbar-title">
              <strong>{{ selectedCount }}</strong> of {{ writableTags.length }} tag(s) selected
            </p>
            <div class="tag-toolbar-actions">
              <button type="button" class="btn btn-secondary tag-action-btn" @click="selectAll">
                Select All
              </button>
              <button
                type="button"
                class="btn btn-secondary tag-action-btn"
                @click="clearSelection"
              >
                Clear
              </button>
            </div>
          </div>

          <section v-for="file in preview.files" :key="file.fileId" class="tag-file">
            <header v-if="preview.files.length > 1" class="tag-file-header">
              <PhFileAudio />
              <span>{{ file.name }}</span>
            </header>

            <p v-if="file.error" class="tag-file-error">
              <PhWarningCircle />
              {{ file.error }}
            </p>

            <ul v-else class="tag-change-list">
              <li
                v-for="change in visibleChanges(file)"
                :key="change.tag"
                class="tag-change"
                :class="{ 'tag-change--inactive': !isWritable(change) }"
              >
                <label class="tag-change-header">
                  <input
                    type="checkbox"
                    class="tag-checkbox"
                    :disabled="!isWritable(change)"
                    :checked="selected.has(change.tag)"
                    @change="toggle(change.tag)"
                  />
                  <span class="tag-change-label">{{ change.label }}</span>
                  <code class="tag-change-key">{{ change.tag }}</code>
                  <span v-if="isEdited(change)" class="tag-edited-badge">edited</span>
                  <span class="tag-change-reason">{{ reasonFor(change) }}</span>
                </label>

                <div class="tag-change-values">
                  <div class="tag-value">
                    <span class="tag-value-label">Now</span>
                    <span class="tag-value-text tag-value-text--current">{{
                      change.current || '—'
                    }}</span>
                  </div>
                  <div class="tag-value">
                    <span class="tag-value-label" :for="`tag-value-${file.fileId}-${change.tag}`">
                      After
                    </span>
                    <textarea
                      v-if="change.isLongText"
                      :id="`tag-value-${file.fileId}-${change.tag}`"
                      class="tag-value-input tag-value-input--long"
                      rows="5"
                      :disabled="change.action === 'NotConfigured'"
                      :value="valueFor(change)"
                      @input="edit(change, ($event.target as HTMLTextAreaElement).value)"
                    ></textarea>
                    <input
                      v-else
                      :id="`tag-value-${file.fileId}-${change.tag}`"
                      type="text"
                      class="tag-value-input"
                      :disabled="change.action === 'NotConfigured'"
                      :value="valueFor(change)"
                      @input="edit(change, ($event.target as HTMLInputElement).value)"
                    />
                    <button
                      v-if="isEdited(change)"
                      type="button"
                      class="tag-value-revert"
                      @click="revert(change)"
                    >
                      <PhArrowCounterClockwise :size="13" />
                      Undo edit
                    </button>
                  </div>
                </div>
              </li>
            </ul>
          </section>

          <label class="tag-show-all">
            <input type="checkbox" v-model="showUnchanged" />
            Show tags that will not change
          </label>
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
            :disabled="loading || queueing || selectedCount === 0"
            @click="confirm"
          >
            <PhSpinner v-if="queueing" class="ph-spin" :size="16" />
            <PhTag v-else :size="16" />
            Write {{ selectedCount }} tag(s)
          </button>
        </template>
      </ModalFooter>
    </template>
  </Modal>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  PhArrowCounterClockwise,
  PhCheckCircle,
  PhFileAudio,
  PhInfo,
  PhSpinner,
  PhTag,
  PhWarningCircle,
  PhX,
} from '@phosphor-icons/vue'
import { Modal, ModalBody, ModalFooter, ModalHeader } from '@/components/feedback'
import { apiService } from '@/services/api'
import { logger } from '@/utils/logger'
import type { TagChangePreview, TagPreview, TagPreviewFile } from '@/types'

const props = defineProps<{
  visible: boolean
  audiobookId: number | null
}>()

const emit = defineEmits<{
  (event: 'close'): void
  (event: 'confirm', payload: { tags: string[]; values: Record<string, string> }): void
}>()

const loading = ref(false)
const queueing = ref(false)
const error = ref<string | null>(null)
const preview = ref<TagPreview | null>(null)
const selected = ref(new Set<string>())
const showUnchanged = ref(false)

/**
 * What each tag will be written as. Seeded from the server's proposal and then owned by
 * the operator: a provider gets a series position wrong often enough that a preview you
 * can only accept or reject would leave correcting one book as a settings exercise.
 */
const values = ref<Record<string, string>>({})

const proposedFor = (change: TagChangePreview) => change.proposed ?? ''

const valueFor = (change: TagChangePreview) =>
  values.value[change.tag] ?? proposedFor(change)

const isEdited = (change: TagChangePreview) =>
  change.tag in values.value && values.value[change.tag] !== proposedFor(change)

/**
 * A tag can be written when the preview says so, or when the operator has typed
 * something for it. The one exception is a tag the mapping is set never to write: that
 * is a standing decision, and a preview is not the place to reverse it by accident.
 */
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

function edit(change: TagChangePreview, value: string) {
  values.value = { ...values.value, [change.tag]: value }

  // Typing a value into a row the preview would have skipped is how an operator says
  // they want it written; ticking it as well would be a second, pointless step.
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

const selectedCount = computed(() => selected.value.size)

const visibleChanges = (file: TagPreviewFile) =>
  showUnchanged.value ? file.changes : file.changes.filter(isWritable)

const changeByTag = computed(() => {
  const map = new Map<string, TagChangePreview>()
  for (const file of preview.value?.files ?? []) {
    for (const change of file.changes) {
      map.set(change.tag, change)
    }
  }
  return map
})

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
  preview.value = null

  try {
    const result = await apiService.previewTags(props.audiobookId)
    preview.value = result
    // A fresh proposal, so nothing is edited yet.
    values.value = {}
    // Everything the mapping would write starts ticked: the operator is narrowing a
    // proposal, not assembling one from nothing.
    selected.value = new Set(
      result.files.flatMap((file) => file.changes.filter(isWritable).map((change) => change.tag)),
    )
  } catch (err) {
    logger.warn('Failed to preview tags', err)
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

function handleClose() {
  emit('close')
}

function confirm() {
  if (selectedCount.value === 0) return
  queueing.value = true
  try {
    const tags = Array.from(selected.value)

    // Send the values the operator actually saw, edited or not, rather than only the
    // tag names. The write is then the diff they approved instead of whatever the
    // patterns happen to render when the worker gets to it.
    const chosen: Record<string, string> = {}
    for (const tag of tags) {
      const change = changeByTag.value.get(tag)
      if (change) {
        chosen[tag] = valueFor(change)
      }
    }

    emit('confirm', { tags, values: chosen })
  } finally {
    queueing.value = false
  }
}

watch(
  () => [props.visible, props.audiobookId] as const,
  ([visible]) => {
    if (visible) {
      showUnchanged.value = false
      void load()
    }
  },
  { immediate: true },
)
</script>

<style scoped>
.tag-modal-body {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.tag-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  padding: 2.5rem 1rem;
  text-align: center;
  color: var(--text-secondary, #adb5bd);
}

.tag-icon {
  width: 32px;
  height: 32px;
}

.tag-error {
  color: #ff6b6b;
}

.tag-success {
  color: #51cf66;
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

.tag-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.tag-toolbar-title {
  margin: 0;
  font-size: 0.875rem;
  color: var(--text-secondary, #adb5bd);
}

.tag-toolbar-actions {
  display: flex;
  gap: 0.5rem;
}

.tag-action-btn {
  padding: 0.35rem 0.75rem;
  font-size: 0.8125rem;
}

.tag-file + .tag-file {
  margin-top: 1rem;
}

.tag-file-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.875rem;
  font-weight: 600;
  margin-bottom: 0.5rem;
  color: var(--text-primary, #f8f9fa);
}

.tag-file-error {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: #ff6b6b;
  font-size: 0.875rem;
  margin: 0;
}

.tag-change-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.tag-change {
  border: 1px solid var(--border-color, #343a40);
  border-radius: 6px;
  padding: 0.75rem;
  background-color: var(--bg-secondary, #212529);
}

.tag-change--inactive {
  opacity: 0.65;
}

.tag-change-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  cursor: pointer;
}

.tag-checkbox {
  flex-shrink: 0;
}

.tag-change-label {
  font-weight: 600;
  font-size: 0.9375rem;
}

.tag-change-key {
  font-size: 0.75rem;
  padding: 0.1rem 0.35rem;
  border-radius: 4px;
  background-color: var(--bg-tertiary, #2b3035);
  color: var(--text-secondary, #adb5bd);
}

.tag-change-reason {
  font-size: 0.8125rem;
  color: var(--text-secondary, #adb5bd);
  margin-left: auto;
}

.tag-change-values {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 0.75rem;
  margin-top: 0.6rem;
}

@media (max-width: 640px) {
  .tag-change-values {
    grid-template-columns: minmax(0, 1fr);
  }

  .tag-change-reason {
    margin-left: 0;
    width: 100%;
  }
}

.tag-value {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  min-width: 0;
}

.tag-value-label {
  font-size: 0.6875rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-secondary, #adb5bd);
}

.tag-value-text {
  font-size: 0.8125rem;
  word-break: break-word;
  /* A blurb runs to several paragraphs; showing all of it would bury the other
     twenty tags, and its first lines are enough to recognise. */
  max-height: 5.5rem;
  overflow-y: auto;
  white-space: pre-wrap;
}

.tag-value-text--current {
  color: var(--text-secondary, #adb5bd);
}

.tag-value-input {
  width: 100%;
  font-size: 0.8125rem;
  color: #51cf66;
  padding: 0.35rem 0.5rem;
  border: 1px solid var(--border-color, #343a40);
  border-radius: 4px;
  background-color: var(--bg-tertiary, #2b3035);
  font-family: inherit;
}

.tag-value-input--long {
  resize: vertical;
  min-height: 5rem;
  line-height: 1.45;
}

.tag-value-input:disabled {
  opacity: 0.5;
  color: var(--text-secondary, #adb5bd);
}

.tag-value-input:focus {
  outline: none;
  border-color: #51cf66;
}

.tag-edited-badge {
  font-size: 0.6875rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 0.1rem 0.35rem;
  border-radius: 4px;
  background-color: rgba(255, 212, 59, 0.15);
  border: 1px solid rgba(255, 212, 59, 0.35);
  color: #ffd43b;
}

.tag-value-revert {
  align-self: flex-start;
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  margin-top: 0.3rem;
  padding: 0;
  border: none;
  background: none;
  cursor: pointer;
  font-size: 0.75rem;
  color: var(--text-secondary, #adb5bd);
}

.tag-value-revert:hover {
  color: var(--text-primary, #f8f9fa);
}

.tag-show-all {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.8125rem;
  color: var(--text-secondary, #adb5bd);
  cursor: pointer;
}
</style>

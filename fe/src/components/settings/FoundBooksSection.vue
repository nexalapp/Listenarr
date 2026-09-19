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
  <div class="form-section">
    <h3><PhFolderOpen /> Found Books</h3>
    <div class="form-body">
      <FormRow
        label="Watch Folders"
        help="Folders scanned for complete books that are not in the library: a pack that arrived with more than was asked for, a manual download, an import that was left behind. Leave empty to watch every enabled download client's completed path, translated through its remote path mappings."
      >
        <ul v-if="folders.length > 0" class="watch-list">
          <li v-for="(folder, index) in folders" :key="folder" class="watch-item">
            <span class="watch-path" :title="folder">{{ folder }}</span>
            <button
              type="button"
              class="icon-btn btn-secondary"
              title="Stop watching this folder"
              aria-label="Stop watching this folder"
              @click="removeFolder(index)"
            >
              <PhX :size="14" />
            </button>
          </li>
        </ul>
        <p v-else class="found-note">
          No watch folders: your download clients' completed folders are used.
        </p>
        <div class="path-input-row">
          <input
            v-model="draft"
            type="text"
            class="form-input"
            placeholder="Select or enter a folder..."
            @keydown.enter.prevent="addFolder(draft)"
          />
          <button
            type="button"
            class="icon-btn btn-secondary btn-inline-browse"
            title="Browse for folder"
            aria-label="Browse for folder"
            @click="showBrowser = true"
          >
            <PhFolder :size="16" />
          </button>
          <button
            type="button"
            class="btn btn-secondary btn-sm"
            :disabled="!draft.trim()"
            @click="addFolder(draft)"
          >
            Add
          </button>
        </div>
        <FolderBrowserModal
          v-model:visible="showBrowser"
          v-model:modelValue="draft"
          :show-input="false"
          @update:modelValue="(value: string) => addFolder(value)"
        />
      </FormRow>

      <FormRow
        label="Scan Interval"
        help="Minutes between scans of the watch folders. Zero turns the periodic scan off; the Found tab's own Scan button still works. An unchanged file is not read again, so a scan of a settled folder is cheap."
      >
        <input
          :value="settings.foundBooksScanIntervalMinutes ?? 60"
          type="number"
          min="0"
          max="1440"
          @input="
            (e) =>
              updateField(
                'foundBooksScanIntervalMinutes',
                Math.max(0, Number((e.target as HTMLInputElement).value || 0)),
              )
          "
        />
      </FormRow>

      <FormRow
        label="Add Automatically"
        help="After each scan, add every found book that is complete, not in the library, and matched to the catalogue beyond doubt: an ASIN in its own tags, or a title and author that agree exactly. Files are moved into the default library folder and the book is monitored. Anything less certain — a book numbered from one with no total, a near match — waits on the Found tab."
      >
        <label class="found-toggle">
          <input
            type="checkbox"
            :checked="settings.foundBooksAutoAdd ?? false"
            @change="
              (e) => updateField('foundBooksAutoAdd', (e.target as HTMLInputElement).checked)
            "
          />
          <span>Add certain matches without asking</span>
        </label>
        <p class="found-note">
          Every automatic add is written to history and marked "auto" on the Found tab's Done list.
          Nothing is ever deleted automatically.
        </p>
      </FormRow>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { PhFolder, PhFolderOpen, PhX } from '@phosphor-icons/vue'
import FolderBrowserModal from '@/components/feedback/FolderBrowserModal.vue'
import FormRow from '@/components/settings/FormRow.vue'
import type { ApplicationSettings } from '@/types'

const props = defineProps<{ settings: Partial<ApplicationSettings> }>()
const emit = defineEmits<{
  'update:settings': [value: Partial<ApplicationSettings>]
}>()

const updateField = <K extends keyof ApplicationSettings>(
  key: K,
  value: ApplicationSettings[K],
) => {
  emit('update:settings', { ...props.settings, [key]: value })
}

const folders = computed(() => props.settings.foundBooksWatchFolders ?? [])
const draft = ref('')
const showBrowser = ref(false)

function addFolder(value: string) {
  const path = value.trim()
  if (!path) return
  if (!folders.value.includes(path)) {
    updateField('foundBooksWatchFolders', [...folders.value, path])
  }
  draft.value = ''
}

function removeFolder(index: number) {
  updateField(
    'foundBooksWatchFolders',
    folders.value.filter((_, i) => i !== index),
  )
}
</script>

<style scoped>
.watch-list {
  list-style: none;
  padding: 0;
  margin: 0 0 0.5rem;
}

.watch-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.25rem 0;
}

.watch-path {
  flex: 1;
  font-family: var(--font-mono, monospace);
  font-size: 0.85rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.path-input-row {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}

.path-input-row .form-input {
  flex: 1;
}

.found-toggle {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
}

.found-toggle input {
  width: 1rem;
  height: 1rem;
}

.found-note {
  margin: 0.5rem 0 0;
  font-size: 0.8125rem;
  color: var(--text-secondary, #adb5bd);
  line-height: 1.5;
}
</style>

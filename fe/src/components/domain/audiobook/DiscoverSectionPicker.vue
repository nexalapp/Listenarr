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
  <div class="picker" @keydown.esc="close">
    <button
      ref="trigger"
      type="button"
      class="btn btn-secondary picker-trigger"
      :aria-expanded="open"
      aria-haspopup="true"
      @click="toggle"
    >
      {{ label }}
      <span v-if="selectedCount > 0" class="picker-count">{{ selectedCount }}</span>
      <PhCaretDown :size="12" />
    </button>

    <div v-if="open" class="picker-panel" role="dialog" :aria-label="label">
      <LoadingState v-if="loading" :message="`Reading ${sourceName}…`" />

      <template v-else-if="error">
        <p class="picker-error">{{ error }}</p>
        <button type="button" class="link-btn" @click="load">Try again</button>
      </template>

      <template v-else>
        <div class="picker-head">
          <strong>{{ heading }}</strong>
          <input
            v-model="filter"
            type="search"
            class="picker-filter"
            :placeholder="`Filter ${noun}`"
            :aria-label="`Filter ${noun}`"
          />
        </div>

        <ul class="picker-list">
          <li v-for="option in visibleOptions" :key="option.id">
            <label>
              <input
                type="checkbox"
                :value="option.id"
                :checked="isChosen(option.id)"
                @change="toggleOption(option)"
              />
              <span class="picker-name">{{ option.name }}</span>
              <span v-if="option.count" class="picker-meta">{{
                option.count.toLocaleString()
              }}</span>
            </label>
          </li>
          <li v-if="visibleOptions.length === 0" class="picker-empty">
            Nothing matches "{{ filter }}".
          </li>
        </ul>

        <div class="picker-depth">
          <label :for="`${uid}-pages`">Pages to keep loaded</label>
          <input
            :id="`${uid}-pages`"
            v-model.number="pages"
            type="number"
            min="1"
            :max="maxPages"
          />
          <small>{{ depthHelp }}</small>
        </div>

        <div class="picker-actions">
          <button type="button" class="link-btn" @click="close">Cancel</button>
          <button type="button" class="btn btn-primary btn-sm" :disabled="saving" @click="save">
            {{ saving ? 'Saving…' : 'Save' }}
          </button>
        </div>
      </template>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { PhCaretDown } from '@phosphor-icons/vue'
import { LoadingState } from '@/components/base'
import { apiService } from '@/services/api'
import { useToast } from '@/services/toastService'
import { describeApiError } from '@/utils/apiError'
import type { DiscoverSectionOption, DiscoverSelectedSection, DiscoverSource } from '@/types'

const props = defineProps<{
  source: DiscoverSource
  /** The button's text: "Categories" or "Boards". */
  label: string
  /** Plural, lower case, for the filter box and the heading. */
  noun: string
  sourceName: string
  depthHelp: string
  maxPages: number
}>()

const emit = defineEmits<{ saved: [] }>()

const toast = useToast()
const uid = `picker-${Math.random().toString(36).slice(2, 8)}`

const open = ref(false)
const loading = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)
const options = ref<DiscoverSectionOption[]>([])
const chosen = ref<DiscoverSelectedSection[]>([])
const pages = ref(5)
const filter = ref('')
const trigger = ref<HTMLButtonElement | null>(null)

const selectedCount = computed(() => chosen.value.length)
const heading = computed(() => `Include these ${props.noun}`)

const visibleOptions = computed(() => {
  const needle = filter.value.trim().toLowerCase()
  if (!needle) return options.value
  return options.value.filter((option) => option.name.toLowerCase().includes(needle))
})

function isChosen(id: string): boolean {
  return chosen.value.some((section) => section.id === id)
}

function toggleOption(option: DiscoverSectionOption) {
  chosen.value = isChosen(option.id)
    ? chosen.value.filter((section) => section.id !== option.id)
    : [...chosen.value, { id: option.id, name: option.name, slug: option.slug }]
}

function toggle() {
  open.value = !open.value
  if (open.value) void load()
}

function close() {
  open.value = false
  filter.value = ''
}

async function load() {
  loading.value = true
  error.value = null
  try {
    const catalog = await apiService.getDiscoverSections(props.source)
    options.value = catalog.options
    pages.value = catalog.pagesPerSection
    chosen.value = catalog.options
      .filter((option) => catalog.selected.includes(option.id))
      .map((option) => ({ id: option.id, name: option.name, slug: option.slug }))
    if (!catalog.available) {
      error.value = catalog.unavailable ?? 'This source cannot be used yet.'
    }
  } catch (e) {
    error.value = describeApiError(e, 'The list could not be read.')
  } finally {
    loading.value = false
  }
}

async function save() {
  saving.value = true
  try {
    await apiService.saveDiscoverSections(props.source, {
      sections: chosen.value,
      pagesPerSection: pages.value,
    })
    close()
    emit('saved')
  } catch (e) {
    toast.error('Not saved', describeApiError(e, 'The selection could not be saved.'))
  } finally {
    saving.value = false
  }
}

// Clicking anywhere else closes the panel, the way the source sites' own filters do.
function onDocumentClick(event: MouseEvent) {
  if (!open.value) return
  const target = event.target as Node | null
  const root = trigger.value?.parentElement
  if (root && target && !root.contains(target)) close()
}

watch(open, (value) => {
  if (value) document.addEventListener('click', onDocumentClick)
  else document.removeEventListener('click', onDocumentClick)
})

onBeforeUnmount(() => document.removeEventListener('click', onDocumentClick))
</script>

<style scoped>
.picker {
  position: relative;
  display: inline-block;
}

.picker-trigger {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
}

.picker-count {
  background: #3b82f6;
  color: #fff;
  border-radius: 10px;
  padding: 0 0.4rem;
  font-size: 0.7rem;
  line-height: 1.4;
}

.picker-panel {
  position: absolute;
  z-index: 40;
  top: calc(100% + 0.4rem);
  right: 0;
  width: min(420px, 90vw);
  background: #232323;
  border: 1px solid #3a3a3a;
  border-radius: 6px;
  box-shadow: 0 10px 30px rgba(0, 0, 0, 0.6);
  padding: 0.9rem;
}

.picker-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.6rem;
  margin-bottom: 0.6rem;
  color: #eee;
  font-size: 0.85rem;
}

.picker-filter {
  flex: 0 1 11rem;
  background: #1b1b1b;
  border: 1px solid #3a3a3a;
  border-radius: 4px;
  color: #ddd;
  padding: 0.25rem 0.4rem;
  font-size: 0.8rem;
}

.picker-list {
  list-style: none;
  margin: 0;
  padding: 0;
  max-height: 16rem;
  overflow-y: auto;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
  gap: 0.1rem 0.8rem;
}

.picker-list label {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.2rem 0;
  color: #ccc;
  font-size: 0.82rem;
  cursor: pointer;
}

.picker-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.picker-meta {
  margin-left: auto;
  color: #777;
  font-size: 0.72rem;
}

.picker-empty {
  color: #888;
  font-size: 0.8rem;
  padding: 0.4rem 0;
}

.picker-depth {
  display: grid;
  grid-template-columns: auto 5rem;
  align-items: center;
  gap: 0.4rem 0.6rem;
  margin-top: 0.8rem;
  color: #ccc;
  font-size: 0.82rem;
}

.picker-depth input {
  background: #1b1b1b;
  border: 1px solid #3a3a3a;
  border-radius: 4px;
  color: #ddd;
  padding: 0.25rem 0.4rem;
}

.picker-depth small {
  grid-column: 1 / -1;
  color: #888;
  font-size: 0.72rem;
}

.picker-actions {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: 0.8rem;
  margin-top: 0.9rem;
}

.picker-error {
  color: #e5a0a0;
  font-size: 0.85rem;
  margin: 0 0 0.5rem 0;
}

.link-btn {
  background: none;
  border: none;
  color: #6ea8fe;
  cursor: pointer;
  font-size: 0.82rem;
  padding: 0;
}
</style>

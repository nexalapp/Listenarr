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
  <div
    class="audiobook-list-item"
    :class="[`status-${status}`, { selected, 'not-in-library': !inLibrary }]"
    tabindex="0"
    @click="emit('open')"
    @keydown.enter.self="emit('open')"
  >
    <div
      v-if="selectable"
      class="selection-checkbox"
      @click.stop="emit('select-click', $event)"
      @mousedown.prevent
    >
      <input
        type="checkbox"
        :checked="selected"
        @change="emit('select-change', $event)"
        @keydown.space.prevent="emit('select-keydown', $event)"
      />
    </div>
    <div v-else class="selection-checkbox-spacer" />

    <div class="list-thumb-container">
      <div class="audiobook-image-placeholder" :class="{ loaded }">
        <PhBookOpen class="audiobook-placeholder-icon" />
      </div>
      <img
        v-if="imageSrc"
        ref="image"
        class="list-thumb cover-loading-image"
        :class="{ loaded }"
        :src="imageSrc"
        :alt="audiobook.title"
        loading="lazy"
        decoding="async"
        @load="onLoad"
        @error="emit('image-error', $event)"
      />
    </div>

    <div class="list-details">
      <div class="audiobook-title">
        <span v-if="seriesPosition" class="series-position">#{{ seriesPosition }}</span>
        {{ safeText(audiobook.title) }}
      </div>
      <div class="audiobook-author">{{ authorLine }}</div>
      <div v-if="showDetails" class="list-extra-details">
        <slot name="details" />
      </div>
    </div>

    <div class="list-badges">
      <div
        class="status-badge"
        :class="status"
        role="button"
        tabindex="0"
        :aria-label="`Status for ${audiobook.title}`"
        @click.stop="emit('status')"
        @keydown.enter.prevent="emit('status')"
        @keydown.space.prevent="emit('status')"
      >
        {{ statusLabel }}
      </div>
      <div v-if="qualityProfileName" class="quality-profile-badge">
        <PhStar />
        {{ qualityProfileName }}
      </div>
      <MonitoredBadge
        :monitored="!!audiobook.monitored"
        :in-library="inLibrary"
        :busy="monitorBusy"
        @toggle="emit('toggle-monitored')"
      />
      <slot name="badges" />
    </div>

    <div class="list-actions">
      <button
        v-if="canSearch"
        class="action-btn search-btn-small"
        :title="searchTitle"
        @click.stop="emit('search')"
      >
        <PhMagnifyingGlass />
      </button>
      <template v-if="inLibrary">
        <button class="action-btn edit-btn-small" title="Edit" @click.stop="emit('edit')">
          <PhPencil />
        </button>
        <button class="action-btn delete-btn-small" title="Delete" @click.stop="emit('delete')">
          <PhTrash />
        </button>
      </template>
      <button
        v-else
        class="action-btn add-btn-small"
        title="Add to Library"
        @click.stop="emit('add')"
      >
        <PhPlus />
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * A book as a row in a list: the list-layout twin of AudiobookCoverCard, with the
 * same actions under the same rules. See that component for the rules.
 */
import { computed, nextTick, ref, watch } from 'vue'
import {
  PhBookOpen,
  PhMagnifyingGlass,
  PhPencil,
  PhPlus,
  PhStar,
  PhTrash,
} from '@phosphor-icons/vue'
import MonitoredBadge from './MonitoredBadge.vue'
import { safeText } from '@/utils/textUtils'
import type { Audiobook, AudiobookStatus } from '@/types'

const props = withDefaults(
  defineProps<{
    audiobook: Audiobook
    status: AudiobookStatus | 'not-added'
    statusLabel: string
    inLibrary?: boolean
    selected?: boolean
    selectable?: boolean
    showDetails?: boolean
    imageSrc?: string
    imageLoaded?: boolean
    seriesPosition?: string | null
    qualityProfileName?: string | null
    monitorBusy?: boolean
  }>(),
  {
    inLibrary: true,
    selected: false,
    selectable: true,
    showDetails: false,
    // Declared so Vue does not cast the absent boolean to false: undefined means
    // "the row tracks loading itself".
    imageLoaded: undefined,
  },
)

const emit = defineEmits<{
  open: []
  status: []
  'select-click': [event: MouseEvent]
  'select-change': [event: Event]
  'select-keydown': [event: KeyboardEvent]
  edit: []
  delete: []
  add: []
  search: []
  'toggle-monitored': []
  'image-load': []
  'image-error': [event: Event]
}>()

const image = ref<HTMLImageElement | null>(null)
const ownLoaded = ref(false)
const loaded = computed(() => props.imageLoaded ?? ownLoaded.value)

// A protected cover arrives as a data URL swapped in after the first paint, and
// a cached or inline image can be complete before any load event is heard. So
// the source is checked after every change as well as on the event.
watch(
  () => props.imageSrc,
  () => {
    ownLoaded.value = false
    void nextTick(() => {
      const el = image.value
      if (el?.complete && el.naturalWidth > 0) onLoad()
    })
  },
  { immediate: true },
)

function onLoad() {
  if (ownLoaded.value) return
  ownLoaded.value = true
  emit('image-load')
}

const authorLine = computed(
  () => props.audiobook.authors?.map((author) => safeText(author)).join(', ') || 'Unknown Author',
)

const canSearch = computed(() => !props.inLibrary || props.status === 'no-file')
const searchTitle = computed(() =>
  props.inLibrary
    ? 'Search for this book'
    : 'Search for a release; added to the library only if you grab one',
)
</script>

<style scoped>
.audiobook-list-item {
  display: grid;
  grid-template-columns: 40px 64px 1fr auto 160px;
  gap: 12px;
  align-items: center;
  padding: 10px 12px;
  background-color: transparent;
  border-radius: 6px;
  transition:
    background-color 0.12s,
    transform 0.12s;
  border-bottom: 1px solid rgba(255, 255, 255, 0.03);
  cursor: pointer;
}

.audiobook-list-item:hover,
.audiobook-list-item.selected {
  background-color: rgba(255, 255, 255, 0.02);
  transform: translateY(-1px);
}

.audiobook-list-item:focus,
.audiobook-list-item:focus-within {
  outline: 2px solid rgba(var(--brand-rgb), 0.18);
  outline-offset: 2px;
  background-color: rgba(255, 255, 255, 0.02);
}

.audiobook-list-item.not-in-library .list-thumb {
  filter: grayscale(0.5) brightness(0.55);
  transition: filter 0.2s ease;
}

.audiobook-list-item.not-in-library:hover .list-thumb {
  filter: none;
}

.audiobook-list-item.status-no-file .list-thumb-container {
  border-bottom: 3px solid #e74c3c;
}

.audiobook-list-item.status-downloading .list-thumb-container {
  border-bottom: 3px solid #3498db;
  animation: pulse 2s ease-in-out infinite;
}

.audiobook-list-item.status-quality-mismatch .list-thumb-container {
  border-bottom: 3px solid #f39c12;
}

.audiobook-list-item.status-quality-match .list-thumb-container {
  border-bottom: 3px solid #2ecc71;
}

@keyframes pulse {
  0%,
  100% {
    border-bottom-color: #3498db;
  }
  50% {
    border-bottom-color: #5dade2;
  }
}

/* In a list the checkbox is always drawn; only the check itself waits for a selection. */
.selection-checkbox {
  position: relative;
  z-index: 40;
  height: 20px;
  width: 20px;
  margin: 0;
  justify-self: center;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  box-sizing: border-box;
  background-color: transparent;
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  cursor: pointer;
  user-select: none;
}

.selection-checkbox-spacer {
  width: 20px;
}

.selection-checkbox input[type='checkbox'] {
  position: absolute;
  inset: 0;
  margin: 0;
  padding: 0;
  width: 100%;
  height: 100%;
  opacity: 0;
  cursor: pointer;
  z-index: 41;
}

.selection-checkbox::before {
  content: '';
  position: absolute;
  left: 50%;
  top: 50%;
  transform: translate(-50%, -50%);
  width: 14px;
  height: 14px;
  border-radius: 4px;
  border: 2px solid rgba(255, 255, 255, 0.14);
  background: transparent;
  box-sizing: border-box;
  transition:
    border-color 0.12s ease,
    background-color 0.12s ease,
    box-shadow 0.12s ease;
}

.selection-checkbox:hover {
  background-color: rgba(0, 0, 0, 0.3);
  border-color: rgba(255, 255, 255, 0.18);
}

.audiobook-list-item.selected .selection-checkbox::before {
  background-color: var(--brand-500);
  border-color: var(--brand-500);
  box-shadow: 0 0 0 4px rgba(var(--brand-rgb), 0.12);
}

.selection-checkbox input[type='checkbox']:focus-visible {
  outline: 2px solid rgba(var(--brand-rgb), 0.3);
  outline-offset: 2px;
}

.list-thumb-container {
  position: relative;
  width: 56px;
  height: 56px;
  flex-shrink: 0;
  border-radius: 6px;
  overflow: hidden;
}

.list-thumb {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.audiobook-image-placeholder {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.2rem;
  background: linear-gradient(90deg, #242424 0%, #2f343a 50%, #242424 100%);
  background-size: 200% 100%;
  animation: shimmer 1.6s ease infinite;
  color: #9aa4b2;
  opacity: 1;
  transition: opacity 0.2s ease;
  z-index: 1;
  pointer-events: none;
}

.audiobook-image-placeholder.loaded {
  opacity: 0;
}

.audiobook-placeholder-icon {
  width: 1.35rem;
  height: 1.35rem;
}

@keyframes shimmer {
  0% {
    background-position: 200% 0;
  }
  100% {
    background-position: -200% 0;
  }
}

.cover-loading-image {
  position: absolute;
  inset: 0;
  z-index: 2;
  opacity: 0;
  transition: opacity 0.2s ease;
}

.cover-loading-image.loaded {
  opacity: 1;
}

.list-details {
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.audiobook-title {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 14px;
  color: #fff;
}

.series-position {
  display: inline-block;
  margin-right: 6px;
  padding: 0 6px;
  border-radius: 4px;
  background: rgba(52, 152, 219, 0.9);
  font-size: 11px;
  font-weight: 600;
}

.audiobook-author {
  font-size: 12px;
  color: #ccc;
}

.list-extra-details {
  margin-top: 6px;
  color: #e6eef8;
}

.list-extra-details :deep(.detail-line) {
  font-size: 12px;
  color: #bfcad6;
}

.list-badges {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-left: 12px;
  justify-self: start;
}

.list-badges :deep(.monitored-badge) {
  margin-top: 0.5rem;
  margin-left: 0;
}

@media (max-width: 978px) {
  .list-badges {
    flex-direction: column;
    gap: 4px;
    align-items: flex-start;
    margin-left: 0;
    margin-top: 8px;
  }
}

.status-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.25rem 0.5rem;
  margin-right: 0.5rem;
  background-color: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  font-size: 10px;
  font-weight: 500;
  color: #cfcfcf;
  margin-top: 0.5rem;
  cursor: pointer;
  white-space: nowrap;
}

.status-badge.no-file {
  background-color: rgba(231, 76, 60, 0.12);
  border-color: rgba(231, 76, 60, 0.18);
  color: #e74c3c;
}

.status-badge.downloading {
  background-color: rgba(52, 152, 219, 0.1);
  border-color: rgba(52, 152, 219, 0.2);
  color: #3498db;
}

.status-badge.quality-mismatch {
  background-color: rgba(243, 156, 18, 0.1);
  border-color: rgba(243, 156, 18, 0.18);
  color: #f39c12;
}

.status-badge.quality-match {
  background-color: rgba(46, 204, 113, 0.1);
  border-color: rgba(46, 204, 113, 0.18);
  color: #2ecc71;
}

.quality-profile-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  margin-top: 0.5rem;
  padding: 0.25rem 0.5rem;
  margin-right: 0.5rem;
  background-color: rgba(52, 152, 219, 0.2);
  border: 1px solid rgba(52, 152, 219, 0.4);
  border-radius: 6px;
  font-size: 10px;
  font-weight: 500;
  color: #3498db;
  white-space: nowrap;
}

.list-actions {
  display: flex;
  gap: 8px;
  align-items: center;
  justify-self: end;
}

.action-btn {
  padding: 6px 8px;
  background-color: rgba(0, 0, 0, 0.8);
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: 6px;
  color: white;
  cursor: pointer;
  font-size: 14px;
  transition: background-color 0.2s;
}

.action-btn:hover {
  background-color: rgba(0, 0, 0, 0.95);
}

.delete-btn-small {
  background-color: rgba(231, 76, 60, 0.9);
  border-color: rgba(192, 57, 43, 0.5);
}

.delete-btn-small:hover {
  background-color: rgba(192, 57, 43, 1);
}

.edit-btn-small,
.search-btn-small {
  background-color: rgba(52, 152, 219, 0.9);
  border-color: rgba(41, 128, 185, 0.5);
}

.edit-btn-small:hover,
.search-btn-small:hover {
  background-color: rgba(41, 128, 185, 1);
}

.add-btn-small {
  background-color: rgba(46, 204, 113, 0.9);
  border-color: rgba(39, 174, 96, 0.5);
}

.add-btn-small:hover {
  background-color: rgba(39, 174, 96, 1);
}
</style>

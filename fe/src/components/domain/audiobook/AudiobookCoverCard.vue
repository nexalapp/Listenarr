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
  <div class="audiobook-wrapper">
    <div
      class="audiobook-item"
      :class="[
        `status-${status}`,
        {
          selected,
          selectable,
          'not-in-library': !inLibrary,
          'selection-active': selectionActive,
        },
      ]"
      tabindex="0"
      @click="emit('open')"
      @keydown.enter.self="emit('open')"
    >
      <div class="row-click-target" />
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

      <div class="audiobook-poster-container" :class="{ 'show-details': showDetails }">
        <div v-if="seriesPosition" class="series-position-badge">#{{ seriesPosition }}</div>
        <div class="audiobook-image-placeholder" :class="{ loaded }">
          <PhBookOpen class="audiobook-placeholder-icon" />
        </div>
        <img
          v-if="imageSrc"
          ref="image"
          :src="imageSrc"
          :alt="audiobook.title"
          class="audiobook-poster cover-loading-image"
          :class="{ loaded }"
          loading="lazy"
          decoding="async"
          @load="onLoad"
          @error="emit('image-error', $event)"
        />

        <div class="status-overlay">
          <div v-if="!showDetails" class="audiobook-title">{{ safeText(audiobook.title) }}</div>
          <div v-if="!showDetails" class="audiobook-author">{{ authorLine }}</div>
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

        <button
          v-if="canSearch"
          type="button"
          class="overlay-search-btn"
          :title="searchTitle"
          :aria-label="`Search for ${audiobook.title}`"
          @click.stop="emit('search')"
        >
          <PhMagnifyingGlass />
        </button>

        <div class="action-buttons">
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

      <div v-if="showDetails" class="grid-bottom-details">
        <slot name="details">
          <div class="detail-line title">{{ safeText(audiobook.title) }}</div>
          <div class="detail-line small">{{ authorLine }}</div>
          <div class="detail-line small">{{ statusLabel }}</div>
        </slot>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * A book's cover in a grid: the one card every list of books uses, so what a
 * person can do to a book from its cover is the same on the Audiobooks page and
 * on an author's or a series' page.
 *
 * The rules, in one place: the monitored badge is the switch for any library
 * book; the magnifying glass is there for any book with no file - a library
 * book that is missing its file, or a book not yet added - and opens a search
 * for it; a book not in the library gets Add instead of Edit and Delete.
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
    /** A status for the border colour; 'not-added' for a catalogue book the library does not hold. */
    status: AudiobookStatus | 'not-added'
    statusLabel: string
    inLibrary?: boolean
    selected?: boolean
    /** Whether a checkbox is offered at all; books not in the library are not selectable. */
    selectable?: boolean
    /** Something in the list is selected, so every checkbox stays visible. */
    selectionActive?: boolean
    showDetails?: boolean
    imageSrc?: string
    /**
     * Whether the cover has loaded, when the list keeps that itself so a card
     * re-created by virtual scrolling does not fade in again. Left undefined,
     * the card tracks it.
     */
    imageLoaded?: boolean
    seriesPosition?: string | null
    qualityProfileName?: string | null
    monitorBusy?: boolean
  }>(),
  {
    inLibrary: true,
    selected: false,
    selectable: true,
    selectionActive: false,
    showDetails: false,
    // Declared so Vue does not cast the absent boolean to false: undefined means
    // "the card tracks loading itself".
    imageLoaded: undefined,
  },
)

const emit = defineEmits<{
  open: []
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

// A book with nothing on disk is the one worth searching for from here; one that
// has its file, or is on its way, already has what a search would find.
const canSearch = computed(() => !props.inLibrary || props.status === 'no-file')
const searchTitle = computed(() =>
  props.inLibrary
    ? 'Search for this book'
    : 'Search for a release; added to the library only if you grab one',
)
</script>

<style scoped>
.audiobook-wrapper {
  display: flex;
  flex-direction: column;
}

.audiobook-item {
  cursor: pointer;
  transition: transform 0.2s ease;
  position: relative;
}

.audiobook-item:hover {
  transform: scale(1.05);
}

.audiobook-item:focus,
.audiobook-item:focus-within {
  outline: 2px solid rgba(var(--brand-rgb), 0.18);
  outline-offset: 2px;
  background-color: rgba(255, 255, 255, 0.02);
}

.row-click-target {
  position: absolute;
  inset: 0;
  z-index: 10;
  pointer-events: none;
}

/* A catalogue book the library does not hold: dimmed until looked at. */
.audiobook-item.not-in-library .audiobook-poster-container {
  border: 1px dashed rgba(255, 255, 255, 0.18);
}

.audiobook-item.not-in-library .audiobook-poster {
  filter: grayscale(0.5) brightness(0.55);
  transition: filter 0.2s ease;
}

.audiobook-item.not-in-library:hover .audiobook-poster {
  filter: none;
}

.audiobook-item.selected .audiobook-poster-container {
  outline: 3px solid var(--brand-focus);
  outline-offset: 2px;
}

.audiobook-item.status-no-file .audiobook-poster-container {
  border-bottom: 3px solid #e74c3c;
}

.audiobook-item.status-downloading .audiobook-poster-container {
  border-bottom: 3px solid #3498db;
  animation: pulse 2s ease-in-out infinite;
}

.audiobook-item.status-quality-mismatch .audiobook-poster-container {
  border-bottom: 3px solid #f39c12;
}

.audiobook-item.status-quality-match .audiobook-poster-container {
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

/* Selection: a box that appears on hover, on selection, or while anything is selected. */
.selection-checkbox {
  position: absolute;
  top: 8px;
  left: 8px;
  z-index: 40;
  height: 22px;
  width: 22px;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  box-sizing: border-box;
  background-color: rgba(0, 0, 0, 0.45);
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.12s ease;
  opacity: 0;
  user-select: none;
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
  z-index: 1;
}

.selection-checkbox:hover {
  background-color: rgba(0, 0, 0, 0.6);
  border-color: rgba(255, 255, 255, 0.18);
}

.audiobook-item:hover .selection-checkbox,
.audiobook-item.selected .selection-checkbox,
.audiobook-item.selection-active .selection-checkbox {
  opacity: 1;
}

.audiobook-item.selected .selection-checkbox::before {
  background-color: var(--brand-500);
  border-color: var(--brand-500);
  box-shadow: 0 0 0 4px rgba(var(--brand-rgb), 0.12);
}

.selection-checkbox input[type='checkbox']:focus-visible {
  outline: 2px solid rgba(var(--brand-rgb), 0.3);
  outline-offset: 2px;
}

.audiobook-poster-container {
  position: relative;
  aspect-ratio: 1/1;
  border-radius: 6px;
  overflow: hidden;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.5);
}

.series-position-badge {
  position: absolute;
  top: 8px;
  left: 8px;
  z-index: 20;
  padding: 2px 8px;
  border-radius: 6px;
  background: rgba(52, 152, 219, 0.9);
  color: #fff;
  font-size: 12px;
  font-weight: 600;
  pointer-events: none;
}

/* The checkbox sits where the position badge does; when both exist the badge yields. */
.audiobook-item.selectable:hover .series-position-badge,
.audiobook-item.selectable.selection-active .series-position-badge,
.audiobook-item.selected .series-position-badge {
  opacity: 0;
}

.audiobook-image-placeholder {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.45rem;
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
  width: 2.75em;
  height: 2.75em;
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

.audiobook-poster {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

/* The overlay is always there for the badges - a badge that is a switch cannot
   hide until hovered - and grows to show the title and author on hover. */
.status-overlay {
  position: absolute;
  bottom: 0;
  left: 0;
  right: 0;
  background: linear-gradient(transparent, rgba(0, 0, 0, 0.85));
  padding: 8px;
  /* Room for the search button on the right. */
  padding-right: 44px;
  transition: padding 0.2s ease;
  z-index: 101;
  pointer-events: none;
}

.audiobook-poster-container:hover .status-overlay,
.audiobook-poster-container.show-details .status-overlay {
  padding-top: 80px;
}

.status-overlay > :deep(*) {
  pointer-events: auto;
}

.audiobook-title {
  font-size: 13px;
  font-weight: 500;
  color: #fff;
  margin-bottom: 4px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  opacity: 0;
  transition: opacity 0.2s ease;
}

.audiobook-author {
  font-size: 11px;
  color: #ccc;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  opacity: 0;
  transition: opacity 0.2s ease;
}

.audiobook-poster-container:hover .audiobook-title,
.audiobook-poster-container:hover .audiobook-author,
.audiobook-poster-container.show-details .audiobook-title,
.audiobook-poster-container.show-details .audiobook-author {
  opacity: 1;
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
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 100%;
}

/* Bottom-right of the cover, level with the monitored badge on the left. */
.overlay-search-btn {
  position: absolute;
  right: 8px;
  bottom: 8px;
  z-index: 102;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  border: 1px solid rgba(255, 255, 255, 0.25);
  border-radius: 6px;
  background: rgba(0, 0, 0, 0.55);
  color: #fff;
  cursor: pointer;
  font-size: 14px;
  transition:
    background-color 0.15s,
    border-color 0.15s;
}

.overlay-search-btn:hover {
  background: rgba(52, 152, 219, 0.9);
  border-color: rgba(41, 128, 185, 0.6);
}

.action-buttons {
  position: absolute;
  top: 8px;
  right: 8px;
  display: flex;
  gap: 4px;
  opacity: 0;
  transition: opacity 0.2s;
  z-index: 30;
}

.audiobook-item:hover .action-buttons,
.audiobook-item:focus-within .action-buttons {
  opacity: 1;
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

.edit-btn-small {
  background-color: rgba(52, 152, 219, 0.9);
  border-color: rgba(41, 128, 185, 0.5);
}

.edit-btn-small:hover {
  background-color: rgba(41, 128, 185, 1);
}

.add-btn-small {
  background-color: rgba(46, 204, 113, 0.9);
  border-color: rgba(39, 174, 96, 0.5);
}

.add-btn-small:hover {
  background-color: rgba(39, 174, 96, 1);
}

.grid-bottom-details {
  margin-top: 8px;
  color: #e6eef8;
  padding: 0 4px;
  width: 100%;
}

.grid-bottom-details :deep(.detail-line) {
  font-size: 12px;
  color: #bfcad6;
  text-align: center;
}

.grid-bottom-details :deep(.detail-line.title) {
  color: #fff;
  font-weight: 500;
  margin-bottom: 4px;
}
</style>

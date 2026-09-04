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
  <div class="preview" :class="{ 'preview-open': isActive }">
    <button
      class="btn-preview"
      :class="{ active: isActive }"
      :disabled="!rootFolderId"
      :title="buttonTitle"
      :aria-label="buttonTitle"
      @click="toggle"
    >
      <PhSpinner v-if="isLoading" class="ph-spin" :size="14" />
      <PhPause v-else-if="isPlaying" :size="14" weight="fill" />
      <PhPlay v-else :size="14" weight="fill" />
    </button>

    <div v-if="isActive" class="preview-bar">
      <input
        class="preview-seek"
        type="range"
        min="0"
        :max="seekMax"
        step="0.1"
        :value="currentTime"
        :aria-label="`Seek within the ${PREVIEW_SECONDS}-second preview`"
        @input="seek"
      />
      <span class="preview-time">{{ formatTime(currentTime) }} / {{ formatTime(seekMax) }}</span>
      <button class="preview-close" title="Stop preview" aria-label="Stop preview" @click="stop">
        <PhX :size="12" />
      </button>

      <audio
        ref="audioEl"
        :src="src"
        preload="metadata"
        autoplay
        @loadedmetadata="onLoadedMetadata"
        @timeupdate="onTimeUpdate"
        @play="isPlaying = true"
        @pause="isPlaying = false"
        @playing="isLoading = false"
        @waiting="isLoading = true"
        @error="onError"
      />
    </div>
  </div>
</template>

<script lang="ts">
import { ref } from 'vue'

/**
 * How much of a book the import page will play.
 *
 * The endpoint serves the whole file so the browser can seek — an M4B keeps its moov
 * atom at the end and cannot start without reading it — so this limit lives here, in
 * the player, and is a convention of this page rather than a boundary the API enforces.
 * It is enough to answer "is this the book I think it is?" without streaming a whole
 * audiobook across the network.
 */
export const PREVIEW_SECONDS = 120

/**
 * The row currently previewing.
 *
 * This lives in the plain script block rather than in <script setup>, which runs once
 * per instance: the whole point is that every row reads the same value, so starting one
 * preview stops the one already playing. Two books talking over each other tells you
 * nothing about either.
 */
export const activePreviewId = ref<string | null>(null)
</script>

<script setup lang="ts">
import { computed, onBeforeUnmount, watch } from 'vue'
import { PhPlay, PhPause, PhSpinner, PhX } from '@phosphor-icons/vue'
import { apiService } from '@/services/api'
import { useToast } from '@/services/toastService'

const props = defineProps<{
  itemId: string
  path: string
  rootFolderId: number | null
}>()

const toast = useToast()
const audioEl = ref<HTMLAudioElement | null>(null)
const isPlaying = ref(false)
const isLoading = ref(false)
const currentTime = ref(0)
const duration = ref(0)

const isActive = computed(() => activePreviewId.value === props.itemId)

const src = computed(() =>
  props.rootFolderId ? apiService.buildAudioPreviewUrl(props.rootFolderId, props.path) : '',
)

// A book shorter than the preview window should not show dead space on the seek bar.
const seekMax = computed(() =>
  duration.value > 0 ? Math.min(duration.value, PREVIEW_SECONDS) : PREVIEW_SECONDS,
)

const buttonTitle = computed(() =>
  isActive.value ? 'Stop preview' : `Play the first ${PREVIEW_SECONDS / 60} minutes`,
)

function toggle() {
  if (!props.rootFolderId) return

  if (!isActive.value) {
    reset()
    isLoading.value = true
    activePreviewId.value = props.itemId
    return
  }

  const audio = audioEl.value
  if (!audio) return
  if (audio.paused) {
    void audio.play().catch(() => {})
  } else {
    audio.pause()
  }
}

function stop() {
  if (isActive.value) activePreviewId.value = null
  reset()
}

function reset() {
  isPlaying.value = false
  isLoading.value = false
  currentTime.value = 0
  duration.value = 0
}

function onLoadedMetadata() {
  const audio = audioEl.value
  if (!audio) return
  // A stream with no known duration reports Infinity; leave it at zero so the seek bar
  // falls back to the preview window rather than rendering an unusable range.
  duration.value = Number.isFinite(audio.duration) ? audio.duration : 0
}

function onTimeUpdate() {
  const audio = audioEl.value
  if (!audio) return

  if (audio.currentTime >= PREVIEW_SECONDS) {
    audio.pause()
    audio.currentTime = 0
    currentTime.value = 0
    return
  }

  currentTime.value = audio.currentTime
}

function seek(event: Event) {
  const audio = audioEl.value
  if (!audio) return
  const target = Number((event.target as HTMLInputElement).value)
  audio.currentTime = Math.min(target, PREVIEW_SECONDS)
  currentTime.value = audio.currentTime
}

function formatTime(seconds: number): string {
  const safe = Number.isFinite(seconds) && seconds > 0 ? Math.floor(seconds) : 0
  const minutes = Math.floor(safe / 60)
  return `${minutes}:${String(safe % 60).padStart(2, '0')}`
}

function onError() {
  const message =
    props.rootFolderId === null
      ? 'Select a root folder first.'
      : 'This file could not be played. It may have moved, or the browser may not support its format.'
  toast.error('Preview failed', message)
  stop()
}

// Another row taking over tears this player down; make sure it stops making noise first.
watch(isActive, (active) => {
  if (!active) {
    audioEl.value?.pause()
    reset()
  }
})

// Leaving the page mid-playback must not leave audio running against a detached element.
onBeforeUnmount(() => {
  if (isActive.value) activePreviewId.value = null
  audioEl.value?.pause()
})
</script>

<style scoped>
.preview {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  vertical-align: middle;
  /* A flex item of the row's path line: sized to the button while closed, and taking
     the whole line once open so the seek bar has room and the path wraps beneath it. */
  flex: 0 0 auto;
}

.preview-open {
  display: flex;
  flex: 1 1 100%;
}

.btn-preview {
  background: none;
  border: 1px solid #444;
  border-radius: 8px;
  color: #888;
  cursor: pointer;
  padding: 0.2rem 0.35rem;
  flex-shrink: 0;
  display: flex;
  align-items: center;
}

.btn-preview:hover:not(:disabled) {
  border-color: var(--brand-500, #6366f1);
  color: var(--brand-500, #6366f1);
}

.btn-preview.active {
  border-color: var(--brand-500, #6366f1);
  color: var(--brand-500, #6366f1);
}

.btn-preview:disabled {
  opacity: 0.3;
  cursor: not-allowed;
}

.preview-bar {
  display: inline-flex;
  align-items: center;
  gap: 0.45rem;
  flex: 1 1 12rem;
  min-width: 0;
}

.preview-seek {
  flex: 1 1 auto;
  min-width: 5rem;
  accent-color: var(--brand-500, #6366f1);
  cursor: pointer;
  height: 0.85rem;
}

.preview-time {
  color: #888;
  font-size: 0.72rem;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.preview-close {
  background: none;
  border: none;
  color: #777;
  cursor: pointer;
  display: flex;
  align-items: center;
  padding: 0.15rem;
}

.preview-close:hover {
  color: #ccc;
}
</style>

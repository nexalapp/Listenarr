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
  <Modal :visible="true" size="lg" @close="emit('close')">
    <template #header>
      <div class="fm-header">
        <div class="fm-heading">
          <div class="fm-title">Fix match</div>
          <div class="fm-sub">
            {{ folderLabel }} · {{ audioCount }} file{{ audioCount === 1 ? '' : 's' }} ·
            {{ clock(item.totalDurationSeconds) }}
          </div>
        </div>
        <button class="fm-close" type="button" aria-label="Close" @click="emit('close')">✕</button>
      </div>
    </template>

    <div class="fm-search">
      <form class="fm-search-box" @submit.prevent="search">
        <PhMagnifyingGlass :size="14" class="fm-search-icon" />
        <input
          ref="inputEl"
          v-model="query"
          type="text"
          class="fm-search-input"
          placeholder="Title, author, or ASIN"
          spellcheck="false"
        />
        <button type="submit" class="fm-search-btn" :disabled="searching || !query.trim()">
          <PhSpinner v-if="searching" class="ph-spin" :size="14" />
          <span v-else>Search</span>
        </button>
      </form>
      <div class="fm-search-meta">
        <span v-if="searching">Searching the catalogue…</span>
        <span v-else-if="searched">
          {{ candidates.length }} candidate{{ candidates.length === 1 ? '' : 's' }}
        </span>
        <span v-else>Results from the last lookup</span>
        <span v-if="heardLine" class="fm-heard"><PhEar :size="12" /> Heard: {{ heardLine }}</span>
        <button
          type="button"
          class="fm-listen"
          :disabled="listening"
          title="Transcribe the first minute and a half and read the spoken credits; needs transcription on in Settings"
          @click="listen"
        >
          <PhSpinner v-if="listening" class="ph-spin" :size="12" />
          <PhEar v-else :size="12" />
          {{ listening ? 'Listening…' : heardLine ? 'Listen again' : 'Listen to the credits' }}
        </button>
        <span class="fm-hint">An ASIN on its own (B0…) goes straight to that edition.</span>
      </div>
    </div>

    <div class="fm-results">
      <div v-if="!searching && candidates.length === 0" class="fm-empty">
        <PhWarningCircle :size="18" />
        <span>No candidates. Try fewer words, or the ASIN from the Audible page.</span>
      </div>
      <button
        v-for="result in scored"
        :key="result.key"
        type="button"
        class="fm-result"
        :class="{ chosen: chosenKey === result.key }"
        @click="chosenKey = result.key"
      >
        <span class="fm-radio" :class="{ on: chosenKey === result.key }"></span>
        <img
          v-if="result.r.imageUrl"
          :src="getProtectedImageSrc(result.r.imageUrl, placeholderUrl)"
          class="fm-cover"
          alt=""
        />
        <span v-else class="fm-cover fm-cover-empty"></span>
        <span class="fm-body">
          <span class="fm-line1">
            <span class="fm-rtitle">{{ result.r.title }}</span>
            <span class="fm-pct" :class="tone(result.confidence)"
              >{{ pct(result.confidence) }} match</span
            >
          </span>
          <span class="fm-line2">{{ describe(result.r) }}</span>
          <span class="fm-line3">
            {{
              [
                result.r.metadataSource ?? 'Audible',
                year(result.r),
                runtime(result.r),
                result.r.asin ? `ASIN ${result.r.asin}` : null,
              ]
                .filter(Boolean)
                .join(' · ')
            }}
          </span>
          <span v-if="result.runtimeNote" class="fm-line4" :class="tone(result.confidence)">
            {{ result.runtimeNote }}
          </span>
        </span>
      </button>
    </div>

    <template #footer>
      <div class="fm-footer">
        <span class="fm-footer-note">
          {{
            canImport
              ? 'Save keeps the match on the row; Save and import moves the files now.'
              : 'This book is not ready to import; Save keeps the match on the row.'
          }}
        </span>
        <button type="button" class="btn btn-secondary btn-sm" @click="emit('close')">
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-secondary btn-sm"
          :disabled="!chosen"
          @click="save(false)"
        >
          Save match
        </button>
        <button
          type="button"
          class="btn btn-primary btn-sm"
          :disabled="!chosen || !canImport"
          @click="save(true)"
        >
          Save and import
        </button>
      </div>
    </template>
  </Modal>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { PhEar, PhMagnifyingGlass, PhSpinner, PhWarningCircle } from '@phosphor-icons/vue'
import { Modal } from '@/components/feedback'
import { useProtectedImages } from '@/composables/useProtectedImages'
import { apiService } from '@/services/api'
import { getPlaceholderUrl } from '@/utils/placeholder'
import { clock, describeRuntimeDelta, matchConfidence } from '@/utils/foundBookMatch'
import { folderName, useFoundBooksStore } from '@/stores/foundBooks'
import { useToast } from '@/services/toastService'
import type { FoundBook, SearchResult } from '@/types'

const props = defineProps<{
  item: FoundBook
  candidates: SearchResult[]
  selected: SearchResult | null
  canImport: boolean
}>()
const emit = defineEmits<{
  close: []
  save: [result: SearchResult, candidates: SearchResult[], andImport: boolean]
}>()

const { getProtectedImageSrc } = useProtectedImages()
const placeholderUrl = getPlaceholderUrl()
const inputEl = ref<HTMLInputElement | null>(null)

const ASIN = /^[A-Z0-9]{10}$/i

const query = ref(
  props.item.asin ?? [props.item.author, props.item.title].filter(Boolean).join(' ') ?? '',
)
const candidates = ref<SearchResult[]>([...props.candidates])
const searching = ref(false)
const searched = ref(false)
const chosenKey = ref<string | null>(props.selected ? keyOf(props.selected) : null)

const store = useFoundBooksStore()
const toast = useToast()
const listening = ref(false)
const heard = ref<{ title?: string | null; author?: string | null; narrator?: string | null }>({
  title: props.item.heardTitle,
  author: props.item.heardAuthor,
  narrator: props.item.heardNarrator,
})
const heardLine = computed(() =>
  [
    heard.value.title,
    heard.value.author ? `by ${heard.value.author}` : null,
    heard.value.narrator ? `read by ${heard.value.narrator}` : null,
  ]
    .filter(Boolean)
    .join(' '),
)

/**
 * Ask the server to hear the opening credits. What the narrator says becomes the
 * search — a spoken "Fearless, by Jack Campbell" beats a folder called by a hash.
 */
async function listen() {
  listening.value = true
  try {
    const result = await store.listen(props.item.id)
    if (!result) {
      toast.error(
        'Could not listen',
        store.matchState(props.item.id).error ?? 'Transcription is not available.',
      )
      return
    }
    heard.value = result
    if (!result.title && !result.author) {
      toast.info('Nothing heard', 'No credits were recognised in the first minute and a half.')
      return
    }
    query.value = [result.author, result.title].filter(Boolean).join(' ')
    await search()
  } finally {
    listening.value = false
  }
}

const folderLabel = computed(() => folderName(props.item.bookFolder))
const audioCount = computed(() => props.item.files.filter((f) => f.isAudio).length)

const scored = computed(() =>
  candidates.value
    .map((r) => ({
      key: keyOf(r),
      r,
      confidence: matchConfidence(r, props.item),
      runtimeNote: describeRuntimeDelta(r, props.item),
    }))
    .sort((a, b) => b.confidence - a.confidence),
)
const chosen = computed(() => scored.value.find((s) => s.key === chosenKey.value)?.r ?? null)

function keyOf(r: SearchResult): string {
  return r.asin ?? r.id ?? `${r.title}|${r.authors?.[0]?.name ?? ''}`
}

async function search() {
  const q = query.value.trim()
  if (!q) return
  searching.value = true
  try {
    const results = await apiService.advancedSearch(
      ASIN.test(q) ? { asin: q, cap: 5 } : { title: q, cap: 8 },
    )
    candidates.value = results
    searched.value = true
    if (!chosen.value && results.length > 0) chosenKey.value = keyOf(scored.value[0]!.r)
  } catch {
    candidates.value = []
    searched.value = true
  } finally {
    searching.value = false
  }
}

function save(andImport: boolean) {
  if (!chosen.value) return
  emit('save', chosen.value, candidates.value, andImport)
}

function describe(r: SearchResult): string {
  const author = r.authors?.[0]?.name
  const series = r.series ? `${r.series}${r.seriesNumber ? ` #${r.seriesNumber}` : ''}` : null
  const narrator = r.narrators?.[0]?.name ?? r.narrator
  return [author, series, narrator].filter(Boolean).join(' · ')
}

function year(r: SearchResult): string | null {
  return r.releaseDate?.slice(0, 4) ?? r.publishDate?.slice(0, 4) ?? null
}

function runtime(r: SearchResult): string | null {
  const seconds = r.runtime ?? (r.lengthMinutes ? r.lengthMinutes * 60 : undefined)
  return seconds ? clock(seconds) : null
}

function pct(confidence: number): string {
  return `${Math.round(confidence * 100)}%`
}

function tone(confidence: number): string {
  return confidence >= 0.75 ? 'good' : confidence >= 0.5 ? 'warn' : 'dim'
}

onMounted(() => {
  inputEl.value?.focus()
  if (candidates.value.length === 0) void search()
})
</script>

<style scoped>
.fm-header {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  width: 100%;
}

.fm-heading {
  flex: 1;
  min-width: 0;
}

.fm-title {
  font-weight: 600;
  font-size: 16px;
  color: #e8eaed;
}

.fm-sub {
  margin-top: 4px;
  font-family: ui-monospace, Menlo, monospace;
  font-size: 12px;
  color: #79828c;
}

.fm-close {
  background: none;
  border: none;
  color: #8b939d;
  font-size: 17px;
  cursor: pointer;
}

.fm-search {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-bottom: 14px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.07);
}

.fm-search-box {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 6px 8px 6px 12px;
  border-radius: 7px;
  border: 1px solid rgba(255, 255, 255, 0.14);
  background: rgba(255, 255, 255, 0.03);
}

.fm-search-icon {
  color: #69727c;
}

.fm-search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  color: #e8eaed;
  font-size: 13.5px;
}

.fm-search-btn {
  padding: 5px 12px;
  border-radius: 5px;
  border: none;
  background: #2a78d6;
  color: #fff;
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  min-width: 64px;
}

.fm-search-btn:disabled {
  opacity: 0.5;
  cursor: default;
}

.fm-search-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 11.5px;
  color: #79828c;
}

.fm-hint {
  margin-left: auto;
  color: #5aa2f5;
}

.fm-heard {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: #8fd39f;
}

.fm-listen {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 3px 9px;
  border-radius: 5px;
  border: 1px solid rgba(255, 255, 255, 0.14);
  background: none;
  color: #c3cad2;
  font-size: 11.5px;
  cursor: pointer;
}

.fm-listen:disabled {
  opacity: 0.6;
  cursor: default;
}

.fm-results {
  display: flex;
  flex-direction: column;
  max-height: 420px;
  overflow: auto;
  margin: 8px -1.25rem 0;
}

.fm-empty {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 18px 22px;
  color: #9aa3ad;
  font-size: 13px;
}

.fm-result {
  display: flex;
  gap: 14px;
  align-items: flex-start;
  padding: 13px 22px;
  background: none;
  border: none;
  border-left: 2px solid transparent;
  text-align: left;
  cursor: pointer;
  color: inherit;
}

.fm-result:hover {
  background: rgba(255, 255, 255, 0.03);
}

.fm-result.chosen {
  background: rgba(42, 120, 214, 0.1);
  border-left-color: #2a78d6;
}

.fm-radio {
  width: 15px;
  height: 15px;
  margin-top: 3px;
  flex: none;
  border-radius: 50%;
  border: 1px solid rgba(255, 255, 255, 0.22);
  box-sizing: border-box;
}

.fm-radio.on {
  border: 4px solid #2a78d6;
  background: #fff;
}

.fm-cover {
  width: 62px;
  height: 62px;
  flex: none;
  border-radius: 5px;
  object-fit: cover;
}

.fm-cover-empty {
  background: repeating-linear-gradient(135deg, #2b3038 0 5px, #23272e 5px 10px);
}

.fm-body {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.fm-line1 {
  display: flex;
  align-items: baseline;
  gap: 9px;
}

.fm-rtitle {
  font-weight: 600;
  font-size: 14.5px;
  color: #e8eaed;
}

.fm-pct {
  font-family: ui-monospace, Menlo, monospace;
  font-size: 11.5px;
}

.fm-line2 {
  font-size: 12.5px;
  color: #9aa3ad;
}

.fm-line3 {
  font-family: ui-monospace, Menlo, monospace;
  font-size: 11.5px;
  color: #69727c;
}

.fm-line4 {
  font-size: 11.5px;
}

.good {
  color: #8fd39f;
}

.warn {
  color: #e4b64a;
}

.dim {
  color: #79828c;
}

.fm-footer {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
}

.fm-footer-note {
  flex: 1;
  font-size: 12px;
  color: #79828c;
}
</style>

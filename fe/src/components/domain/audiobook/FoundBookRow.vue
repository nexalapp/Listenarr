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
  <div class="fr" :class="{ selected: match.selected, waiting: !ready, busy: match.busy }">
    <label class="fr-check" :class="{ disabled: !ready }">
      <input
        type="checkbox"
        :checked="match.selected"
        :disabled="!ready || match.busy"
        @change="store.setSelected(item.id, ($event.target as HTMLInputElement).checked)"
      />
    </label>

    <div class="fr-book">
      <img
        v-if="coverUrl"
        :src="getProtectedImageSrc(coverUrl, placeholderUrl)"
        class="fr-cover"
        alt=""
      />
      <span v-else class="fr-cover" :class="item.title ? 'fr-cover-blank' : 'fr-cover-none'"></span>
      <div class="fr-book-text">
        <div class="fr-title" :class="{ mono: !item.title }" :title="displayTitle">
          {{ displayTitle }}
        </div>
        <div class="fr-meta" :class="{ dim: !metaLine }">
          {{ metaLine || 'No usable tags — grouped by folder only' }}
        </div>
        <div
          v-if="item.heardTitle || item.heardAuthor"
          class="fr-heard"
          title="What the narrator says in the opening credits"
        >
          <PhEar :size="12" /> Heard:
          {{
            [
              item.heardTitle,
              item.heardAuthor ? `by ${item.heardAuthor}` : null,
              item.heardNarrator ? `read by ${item.heardNarrator}` : null,
            ]
              .filter(Boolean)
              .join(' ')
          }}
        </div>
        <div class="fr-folder" :title="item.bookFolder">
          {{ folderLabel
          }}<template v-if="folderCount > 1"> · {{ folderCount }} folders grouped by tags</template>
        </div>
      </div>
    </div>

    <div class="fr-files">
      <span class="fr-files-main"
        >{{ item.audioFileCount }} file{{ item.audioFileCount === 1 ? '' : 's'
        }}<template v-if="item.format"> · {{ item.format }}</template></span
      >
      <span>{{ sizeLabel }} · {{ clock(item.totalDurationSeconds) }}</span>
      <span class="fr-files-actions">
        <AudioPreviewPlayer
          :preview-id="`found-${item.id}`"
          :src="
            firstAudioIndex >= 0 ? apiService.buildFoundBookAudioUrl(item.id, firstAudioIndex) : ''
          "
          disabled-title="No audio file to play"
        />
        <button type="button" class="fr-link" @click="showFiles = !showFiles">
          {{ showFiles ? 'Hide files' : 'Show files' }}
        </button>
      </span>
    </div>

    <div class="fr-match">
      <template v-if="item.state === 'Pending' || item.state === 'Importing'">
        <div v-if="match.isSearching" class="fr-status dim">
          <PhSpinner class="ph-spin" :size="13" /> Searching…
        </div>
        <template v-else-if="match.selectedMatch">
          <div class="fr-status" :class="label === 'matched' ? 'good' : 'warn'">
            <span class="fr-badge" :class="label === 'matched' ? 'good' : 'warn'">{{
              label === 'matched' ? '✓' : '?'
            }}</span>
            {{ label === 'matched' ? 'Matched' : 'Low confidence' }}
          </div>
          <div class="fr-status-sub">
            <template v-if="label === 'matched'"
              >{{ match.selectedMatch.metadataSource ?? 'Audible' }} ·
              {{ pct }} confidence</template
            >
            <template v-else
              >{{ match.candidates.length }} candidate{{
                match.candidates.length === 1 ? '' : 's'
              }}
              · {{ pct }}</template
            >
          </div>
        </template>
        <template v-else>
          <div class="fr-status bad">
            <span class="fr-badge bad">!</span>
            {{
              match.searchFailed
                ? 'Lookup failed'
                : match.hasSearched
                  ? 'No match'
                  : 'Not looked up'
            }}
          </div>
          <div class="fr-status-sub">
            <template v-if="item.author">Filenames suggest {{ item.author }}</template>
            <template v-else-if="match.searchFailed"
              >The catalogue did not answer; try again</template
            >
            <template v-else>Nothing to search by</template>
          </div>
        </template>
      </template>
      <template v-else-if="item.state === 'Blocked'">
        <div class="fr-status warn"><span class="fr-badge warn">…</span> Waiting</div>
        <div class="fr-status-sub">{{ item.blockedReason }}</div>
      </template>
      <template v-else>
        <div class="fr-status dim">
          {{ item.state }}<Pill v-if="item.autoAdded" variant="info" size="small">auto</Pill>
        </div>
        <RouterLink
          v-if="item.matchedAudiobookId"
          :to="`/books/${item.matchedAudiobookId}`"
          class="fr-link"
          >Open in library</RouterLink
        >
      </template>

      <div v-if="item.completeness === 'Complete'" class="fr-complete">
        <span class="fr-chip blue">Complete</span>
        <span class="fr-status-sub">{{ partsLabel }}</span>
      </div>
      <div v-else-if="item.completeness === 'Unknown'" class="fr-complete">
        <span class="fr-chip grey" :title="item.completenessReason ?? undefined">Unverified</span>
        <span class="fr-status-sub">{{ item.completenessReason }}</span>
      </div>
      <div v-else class="fr-missing">
        <span class="fr-chip amber" :title="item.completenessReason ?? undefined">{{
          item.completeness === 'Corrupt' ? 'Unreadable' : missingLabel
        }}</span>
        <span v-if="progress != null" class="fr-bar"
          ><span class="fr-bar-fill" :style="{ width: `${progress}%` }"></span
        ></span>
        <span class="fr-status-sub mono">{{ item.completenessReason }}</span>
      </div>
      <div v-if="item.libraryStatus === 'InLibrary'" class="fr-status-sub">
        Already in the library<RouterLink
          v-if="item.matchedAudiobookId"
          :to="`/books/${item.matchedAudiobookId}`"
        >
          — open</RouterLink
        >
      </div>
    </div>

    <div class="fr-actions">
      <template v-if="item.state === 'Pending'">
        <button
          type="button"
          class="fr-btn"
          :class="
            label === 'matched' && ready
              ? 'primary'
              : ready && match.selectedMatch
                ? 'outline-blue'
                : 'muted'
          "
          :disabled="!canImport"
          :title="importTitle"
          @click="emit('add', item.id)"
        >
          <PhSpinner v-if="match.busy" class="ph-spin" :size="13" />
          <template v-else>Import</template>
        </button>
        <button
          type="button"
          class="fr-btn"
          :class="!match.selectedMatch && ready ? 'primary' : label === 'low' ? 'amber' : 'outline'"
          :disabled="match.busy"
          @click="emit('fix', item.id)"
        >
          Fix match
        </button>
      </template>
      <template v-else-if="item.state === 'Ignored'">
        <button
          type="button"
          class="fr-btn outline"
          :disabled="match.busy"
          @click="store.decide(item.id, 'restore')"
        >
          Restore
        </button>
      </template>
      <span v-else-if="item.state === 'Importing'" class="fr-status dim"
        ><PhSpinner class="ph-spin" :size="13" /> Importing…</span
      >

      <div v-if="item.state !== 'Imported' && item.state !== 'Discarded'" class="fr-menu-wrap">
        <button
          type="button"
          class="fr-more"
          :aria-expanded="menuOpen"
          aria-label="More actions"
          @click.stop="menuOpen = !menuOpen"
        >
          ⋯
        </button>
        <div v-if="menuOpen" class="fr-menu" @click.stop>
          <button type="button" class="fr-menu-item" @click="toggleFiles">
            {{ showFiles ? 'Hide files' : 'Show files' }}
          </button>
          <button type="button" class="fr-menu-item" @click="copyFolder">Copy folder path</button>
          <span class="fr-menu-sep"></span>
          <button
            v-if="item.state !== 'Ignored' && item.blockedKind !== 'OwnedByDownload'"
            type="button"
            class="fr-menu-item"
            @click="ignoreFromMenu"
          >
            Ignore — hide, keep files
          </button>
          <button
            v-if="item.blockedKind !== 'OwnedByDownload' && item.blockedKind !== 'Downloading'"
            type="button"
            class="fr-menu-item danger"
            @click="discardFromMenu"
          >
            Delete files from disk
          </button>
        </div>
      </div>
    </div>

    <div v-if="showFiles" class="fr-filelist">
      <div
        v-for="(file, i) in item.files"
        :key="file.path"
        class="fr-file"
        :class="{ companion: !file.isAudio, broken: !!file.error }"
        :title="file.path"
      >
        <AudioPreviewPlayer
          v-if="file.isAudio"
          :preview-id="`found-${item.id}-${i}`"
          :src="apiService.buildFoundBookAudioUrl(item.id, audioIndexOf(i))"
          compact
        />
        <span class="fr-file-name">{{ fileName(file.path) }}</span>
        <span class="fr-file-meta">
          <template v-if="file.error">{{ file.error }}</template>
          <template v-else-if="file.durationSeconds">{{ clock(file.durationSeconds) }}</template>
          <template v-else-if="!file.isAudio">companion</template>
        </span>
      </div>
    </div>
    <div v-if="match.error" class="fr-error" :title="match.error">{{ match.error }}</div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { PhEar, PhSpinner } from '@phosphor-icons/vue'
import { Pill } from '@/components/base'
import AudioPreviewPlayer from '@/components/ui/AudioPreviewPlayer.vue'
import { apiService } from '@/services/api'
import { useProtectedImages } from '@/composables/useProtectedImages'
import { useToast } from '@/services/toastService'
import { getPlaceholderUrl } from '@/utils/placeholder'
import { clock, confidenceLabel } from '@/utils/foundBookMatch'
import { folderName, isReady, useFoundBooksStore } from '@/stores/foundBooks'
import type { FoundBook } from '@/types'

const props = defineProps<{ item: FoundBook; canImportInto: boolean }>()
const emit = defineEmits<{ add: [id: number]; fix: [id: number]; discard: [id: number] }>()

const store = useFoundBooksStore()
const toast = useToast()
const { getProtectedImageSrc } = useProtectedImages()
const placeholderUrl = getPlaceholderUrl()

const showFiles = ref(false)
const menuOpen = ref(false)

// The audio endpoint counts audio files only; the list shows companions too.
const firstAudioIndex = computed(() => (props.item.files.some((f) => f.isAudio) ? 0 : -1))
function audioIndexOf(listIndex: number): number {
  return props.item.files.slice(0, listIndex).filter((f) => f.isAudio).length
}

const match = computed(() => store.matchState(props.item.id))
const ready = computed(() => isReady(props.item))
const label = computed(() =>
  confidenceLabel(match.value.selectedMatch ? match.value.confidence : null),
)
const pct = computed(() => `${Math.round((match.value.confidence ?? 0) * 100)}%`)
const coverUrl = computed(() => match.value.selectedMatch?.imageUrl ?? null)

const folderLabel = computed(() => {
  const parts = props.item.bookFolder.replace(/\\/g, '/').split('/').filter(Boolean)
  return parts.slice(-2).join('/')
})
const folderCount = computed(
  () =>
    new Set(
      props.item.files
        .filter((f) => f.isAudio)
        .map((f) => f.path.replace(/\\/g, '/').split('/').slice(0, -1).join('/')),
    ).size,
)
const displayTitle = computed(() => props.item.title?.trim() || folderName(props.item.bookFolder))
const metaLine = computed(() =>
  [
    props.item.author,
    props.item.series
      ? `${props.item.series}${props.item.seriesPosition ? ` #${props.item.seriesPosition}` : ''}`
      : null,
    props.item.narrator ? `read by ${props.item.narrator}` : null,
  ]
    .filter(Boolean)
    .join(' · '),
)
const sizeLabel = computed(() => {
  const gb = props.item.totalBytes / 1024 ** 3
  return gb >= 1 ? `${gb.toFixed(1)} GB` : `${(props.item.totalBytes / 1024 ** 2).toFixed(0)} MB`
})

// "Parts 1–20 of 20." → "20 of 20 parts"; anything else is shown as written.
const partsLabel = computed(() => {
  const m = /Parts 1–(\d+) of (\d+)/.exec(props.item.completenessReason ?? '')
  if (m) return `${m[1]} of ${m[2]} parts`
  return props.item.completenessReason ?? ''
})
const missingCount = computed(() => {
  const m = /Parts \d+–\d+ of (\d+)/.exec(props.item.completenessReason ?? '')
  return m ? Math.max(0, Number(m[1]) - props.item.audioFileCount) : null
})
const missingLabel = computed(() => {
  const m = /of (\d+)/.exec(props.item.completenessReason ?? '')
  if (m && missingCount.value != null) return `Missing ${missingCount.value} of ${m[1]}`
  return 'Missing parts'
})
const progress = computed(() => {
  const m = /of (\d+)/.exec(props.item.completenessReason ?? '')
  if (!m) return null
  const total = Number(m[1])
  return total > 0 ? Math.min(100, Math.round((props.item.audioFileCount / total) * 100)) : null
})

const canImport = computed(
  () =>
    props.canImportInto && ready.value && !match.value.busy && match.value.selectedMatch != null,
)
const importTitle = computed(() => {
  if (!ready.value) return 'Import stays disabled until every part is present'
  if (!props.canImportInto) return 'Choose a library folder first'
  if (!match.value.selectedMatch) return 'Match the book first'
  if (props.item.libraryStatus === 'InLibrary')
    return 'Already in the library; use Fix match and tick "separate book" for a second copy'
  return 'Add to the library and move the files'
})

function fileName(path: string): string {
  return folderName(path)
}

async function copyFolder() {
  menuOpen.value = false
  try {
    await navigator.clipboard.writeText(props.item.bookFolder)
    toast.success('Copied', props.item.bookFolder)
  } catch {
    toast.info('Folder', props.item.bookFolder)
  }
}

function closeMenu() {
  menuOpen.value = false
}

function toggleFiles() {
  showFiles.value = !showFiles.value
  menuOpen.value = false
}

function ignoreFromMenu() {
  menuOpen.value = false
  void store.decide(props.item.id, 'ignore')
}

function discardFromMenu() {
  menuOpen.value = false
  emit('discard', props.item.id)
}

onMounted(() => document.addEventListener('click', closeMenu))
onBeforeUnmount(() => document.removeEventListener('click', closeMenu))
</script>

<style scoped>
.fr {
  display: grid;
  /* The book column takes what is left but no more than a title needs; on a wide
     screen the files and match columns grow instead of a sea of space after the title. */
  grid-template-columns: 34px minmax(260px, 2fr) minmax(190px, 1fr) minmax(220px, 1.2fr) 236px;
  gap: 0 16px;
  padding: 16px 22px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
  align-items: start;
}

.fr.selected {
  background: rgba(42, 120, 214, 0.06);
}

.fr.waiting {
  opacity: 0.72;
}

.fr.busy {
  opacity: 0.6;
}

.fr-check input {
  width: 16px;
  height: 16px;
  margin-top: 2px;
  accent-color: #2a78d6;
}

.fr-check.disabled input {
  opacity: 0.35;
}

.fr-book {
  min-width: 0;
  display: flex;
  gap: 13px;
}

.fr-cover {
  width: 52px;
  height: 52px;
  flex: none;
  border-radius: 5px;
  object-fit: cover;
}

.fr-cover-blank {
  background: repeating-linear-gradient(135deg, #2b3038 0 5px, #23272e 5px 10px);
}

.fr-cover-none {
  background: rgba(255, 255, 255, 0.04);
  border: 1px dashed rgba(255, 255, 255, 0.14);
}

.fr-book-text {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.fr-title {
  font-weight: 600;
  font-size: 15px;
  color: #e8eaed;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.fr-title.mono {
  font-family: ui-monospace, Menlo, monospace;
  color: #c3cad2;
}

.fr-meta {
  font-size: 12.5px;
  color: #9aa3ad;
}

.fr-meta.dim,
.dim {
  color: #79828c;
}

.fr-folder {
  font-family: ui-monospace, Menlo, monospace;
  font-size: 11.5px;
  color: #69727c;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.fr-files {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-family: ui-monospace, Menlo, monospace;
  font-size: 12.5px;
  color: #9aa3ad;
}

.fr-files-main {
  color: #c3cad2;
}

.fr-files-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  min-width: 0;
}

.fr-heard {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: #8fd39f;
}

.fr-link {
  background: none;
  border: none;
  padding: 0;
  color: #5aa2f5;
  font-family: inherit;
  font-size: 12px;
  cursor: pointer;
  text-align: left;
}

.fr-match {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.fr-status {
  display: flex;
  align-items: center;
  gap: 7px;
  font-weight: 500;
  font-size: 13px;
}

.fr-status.good {
  color: #8fd39f;
}

.fr-status.warn {
  color: #e4b64a;
}

.fr-status.bad {
  color: #e08c6a;
}

.fr-badge {
  width: 14px;
  height: 14px;
  border-radius: 50%;
  border: 1px solid currentColor;
  font-size: 9px;
  line-height: 13px;
  text-align: center;
  font-weight: 600;
}

.fr-status-sub {
  font-size: 12px;
  color: #79828c;
}

.fr-status-sub.mono {
  font-family: ui-monospace, Menlo, monospace;
  font-size: 11px;
  color: #69727c;
}

.fr-complete {
  display: flex;
  align-items: center;
  gap: 6px;
}

.fr-missing {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-top: 2px;
}

.fr-chip {
  padding: 2px 7px;
  border-radius: 4px;
  font-size: 11.5px;
  align-self: flex-start;
}

.fr-chip.blue {
  background: rgba(90, 162, 245, 0.14);
  color: #7fb8ff;
}

.fr-chip.amber {
  background: rgba(228, 182, 74, 0.14);
  color: #e4b64a;
  font-weight: 500;
}

.fr-chip.grey {
  background: rgba(255, 255, 255, 0.08);
  color: #9aa3ad;
}

.fr-bar {
  display: block;
  height: 4px;
  border-radius: 2px;
  background: rgba(255, 255, 255, 0.08);
  overflow: hidden;
}

.fr-bar-fill {
  display: block;
  height: 100%;
  background: #e4b64a;
}

.fr-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  justify-content: flex-end;
}

.fr-btn {
  padding: 8px 14px;
  border-radius: 6px;
  font-size: 12.5px;
  font-weight: 500;
  cursor: pointer;
  border: 1px solid rgba(255, 255, 255, 0.14);
  background: none;
  color: #c3cad2;
  min-width: 68px;
}

.fr-btn.primary {
  background: #2a78d6;
  border-color: #2a78d6;
  color: #fff;
}

.fr-btn.outline-blue {
  border-color: rgba(90, 162, 245, 0.5);
  color: #7fb8ff;
}

.fr-btn.amber {
  background: rgba(228, 182, 74, 0.14);
  border-color: rgba(228, 182, 74, 0.4);
  color: #e4b64a;
}

.fr-btn.muted,
.fr-btn:disabled {
  border-color: rgba(255, 255, 255, 0.08);
  color: #575e66;
  background: none;
  cursor: default;
}

.fr-menu-wrap {
  position: relative;
}

.fr-more {
  width: 30px;
  height: 30px;
  border-radius: 6px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  background: none;
  color: #8b939d;
  font-size: 15px;
  cursor: pointer;
}

.fr-menu {
  position: absolute;
  right: 0;
  top: 34px;
  z-index: 20;
  width: 240px;
  padding: 6px;
  border-radius: 8px;
  background: #1b1f23;
  border: 1px solid rgba(255, 255, 255, 0.12);
  box-shadow: 0 12px 30px rgba(0, 0, 0, 0.5);
  display: flex;
  flex-direction: column;
  gap: 1px;
}

.fr-menu-item {
  padding: 9px 12px;
  border-radius: 6px;
  background: none;
  border: none;
  text-align: left;
  font-size: 13px;
  color: #c3cad2;
  cursor: pointer;
}

.fr-menu-item:hover {
  background: rgba(255, 255, 255, 0.06);
}

.fr-menu-item.danger {
  color: #e08c6a;
}

.fr-menu-sep {
  height: 1px;
  background: rgba(255, 255, 255, 0.08);
  margin: 4px 6px;
}

.fr-filelist {
  grid-column: 2 / -1;
  margin-top: 8px;
  padding: 8px 12px;
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.03);
  font-family: ui-monospace, Menlo, monospace;
  font-size: 11.5px;
  max-height: 14rem;
  overflow: auto;
}

.fr-file {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 2px 0;
  color: #c3cad2;
}

.fr-file.companion {
  color: #69727c;
}

.fr-file.broken {
  color: #e08c6a;
}

.fr-file-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.fr-file-meta {
  flex: none;
  color: #69727c;
}

.fr-error {
  grid-column: 2 / -1;
  margin-top: 6px;
  font-size: 12px;
  color: #e08c6a;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 1100px) {
  .fr {
    grid-template-columns: 30px minmax(0, 1fr);
    gap: 8px 12px;
  }

  .fr-files,
  .fr-match,
  .fr-actions {
    grid-column: 2;
  }

  .fr-actions {
    justify-content: flex-start;
  }
}
</style>

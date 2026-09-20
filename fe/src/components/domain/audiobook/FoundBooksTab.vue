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
  <div class="ft">
    <div class="ft-top">
      <div class="ft-tabs">
        <button
          v-for="f in filters"
          :key="f.id"
          type="button"
          class="ft-tab"
          :class="{ active: store.filter === f.id }"
          @click="store.filter = f.id"
        >
          {{ f.label }}
          <span v-if="f.id === 'found' && store.readyItems.length > 0" class="ft-count">{{
            store.readyItems.length
          }}</span>
        </button>
      </div>
      <div class="ft-controls">
        <span class="ft-label">Import into</span>
        <select v-model="selectedFolderId" class="ft-select" aria-label="Library folder">
          <option v-for="f in rootFoldersStore.folders" :key="f.id" :value="f.id">
            {{ f.name || f.path }}
          </option>
        </select>
        <button type="button" class="ft-btn" :disabled="store.scanning" @click="store.scan()">
          <PhSpinner v-if="store.scanning" class="ph-spin" :size="13" />
          {{ store.scanning ? 'Scanning…' : 'Scan now' }}
        </button>
      </div>
    </div>

    <div class="ft-watch">
      <span
        class="ft-dot"
        :class="{ off: !store.watchFolders || store.watchFolders.folders.length === 0 }"
      ></span>
      <template v-if="store.watchFolders && store.watchFolders.folders.length > 0">
        <span>
          Watching
          <span class="ft-path" :title="store.watchFolders.folders[0]!.path">{{
            store.watchFolders.folders[0]!.path
          }}</span>
          <template v-if="store.watchFolders.folders.length > 1">
            and {{ store.watchFolders.folders.length - 1 }} other folder{{
              store.watchFolders.folders.length > 2 ? 's' : ''
            }}
          </template>
        </span>
      </template>
      <span v-else
        >No watch folders — add one, or enable a download client with a download path.</span
      >
      <template v-if="store.lastScanCompletedAt">
        <span class="ft-sep">·</span><span>last scan {{ timeAgo(store.lastScanCompletedAt) }}</span>
      </template>
      <span v-for="w in store.watchFolders?.warnings ?? []" :key="w" class="ft-warning">{{
        w
      }}</span>
      <RouterLink class="ft-manage" :to="{ name: 'settings-found' }">Manage folders</RouterLink>
    </div>

    <div v-if="store.filter === 'found' && store.selectedItems.length > 0" class="ft-bulk">
      <span class="ft-bulk-check">✓</span>
      <span class="ft-bulk-count">{{ store.selectedItems.length }} selected</span>
      <button type="button" class="ft-link" @click="store.selectAllReady()">
        Select all {{ store.readyItems.length }} ready
      </button>
      <button type="button" class="ft-link" @click="store.clearSelection()">Clear</button>
      <div class="ft-bulk-actions">
        <Checkbox v-model="monitorOnAdd">Monitor after import</Checkbox>
        <button type="button" class="ft-btn" :disabled="bulkBusy" @click="ignoreSelected">
          Ignore
        </button>
        <button
          type="button"
          class="ft-btn primary"
          :disabled="bulkBusy || !selectedFolder || selectedAddable.length === 0"
          :title="
            selectedAddable.length === 0 ? 'None of the selected books has a match yet' : undefined
          "
          @click="importSelected"
        >
          <PhSpinner v-if="bulkBusy" class="ph-spin" :size="13" />
          Import {{ selectedAddable.length }} book{{ selectedAddable.length === 1 ? '' : 's' }}
        </button>
      </div>
    </div>

    <LoadingState v-if="store.loading" message="Looking through the watch folders..." />
    <EmptyState v-else-if="store.error" title="Could not load found books" :message="store.error" />

    <template v-else-if="store.filter === 'found'">
      <div
        v-if="store.readyItems.length === 0 && store.incompleteItems.length === 0"
        class="ft-empty"
      >
        <EmptyState
          title="Nothing found"
          message="Every complete book in the watch folders is either in the library or has been decided on. Scan now to look again."
        />
      </div>
      <template v-else>
        <div class="ft-head">
          <span></span><span>Book</span><span>Files</span><span>Match</span><span></span>
        </div>
        <FoundBookRow
          v-for="item in store.readyItems"
          :key="item.id"
          :item="item"
          :can-import-into="!!selectedFolder"
          @add="addOne"
          @fix="openFix"
          @discard="discard"
        />
        <div v-if="store.incompleteItems.length > 0" class="ft-section">
          <span class="ft-section-title">Incomplete</span>
          <span class="ft-section-count">{{ store.incompleteItems.length }}</span>
          <span class="ft-section-note"
            >Missing parts, unreadable files, or still owned by a download. Import stays disabled
            until every part is present.</span
          >
          <button
            type="button"
            class="ft-link ft-section-toggle"
            @click="showIncomplete = !showIncomplete"
          >
            {{ showIncomplete ? 'Collapse' : 'Expand' }}
          </button>
        </div>
        <template v-if="showIncomplete">
          <FoundBookRow
            v-for="item in store.incompleteItems"
            :key="item.id"
            :item="item"
            :can-import-into="!!selectedFolder"
            @add="addOne"
            @fix="openFix"
            @discard="discard"
          />
        </template>
      </template>
    </template>

    <template v-else>
      <EmptyState
        v-if="store.visibleItems.length === 0"
        :title="store.filter === 'ignored' ? 'Nothing ignored' : 'Nothing imported yet'"
        :message="
          store.filter === 'ignored'
            ? 'Books you chose to leave where they are appear here, and can be restored.'
            : 'Imported and discarded books stay here until the next scan confirms they are gone.'
        "
      />
      <template v-else>
        <div class="ft-head">
          <span></span><span>Book</span><span>Files</span><span>Match</span><span></span>
        </div>
        <FoundBookRow
          v-for="item in store.visibleItems"
          :key="item.id"
          :item="item"
          :can-import-into="!!selectedFolder"
          @add="addOne"
          @fix="openFix"
          @discard="discard"
        />
      </template>
    </template>

    <FoundBookMatchModal
      v-if="fixing"
      :item="fixing"
      :candidates="store.matchState(fixing.id).candidates"
      :selected="store.matchState(fixing.id).selectedMatch"
      :can-import="isReady(fixing) && !!selectedFolder"
      @close="fixing = null"
      @save="saveMatch"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { PhSpinner } from '@phosphor-icons/vue'
import { EmptyState, LoadingState } from '@/components/base'
import { Checkbox } from '@/components/form'
import FoundBookRow from '@/components/domain/audiobook/FoundBookRow.vue'
import FoundBookMatchModal from '@/components/domain/audiobook/FoundBookMatchModal.vue'
import { showConfirm } from '@/composables'
import { useToast } from '@/services/toastService'
import { folderName, isReady, useFoundBooksStore, type FoundBookFilter } from '@/stores/foundBooks'
import { useRootFoldersStore } from '@/stores/rootFolders'
import type { FoundBook, SearchResult } from '@/types'

const store = useFoundBooksStore()
const rootFoldersStore = useRootFoldersStore()
const toast = useToast()

const filters: { id: FoundBookFilter; label: string }[] = [
  { id: 'found', label: 'Found' },
  { id: 'ignored', label: 'Ignored' },
  { id: 'imported', label: 'Imported' },
]

const selectedFolderId = ref<number | null>(null)
const selectedFolder = computed(
  () => rootFoldersStore.folders.find((f) => f.id === selectedFolderId.value) ?? null,
)
const monitorOnAdd = ref(true)
const bulkBusy = ref(false)
const showIncomplete = ref(true)
const fixing = ref<FoundBook | null>(null)

const selectedAddable = computed(() =>
  store.selectedItems.filter((item) => store.matchState(item.id).selectedMatch != null),
)

watch(
  () => rootFoldersStore.folders,
  (folders) => {
    if (selectedFolderId.value == null && folders.length > 0) {
      selectedFolderId.value = (folders.find((f) => f.isDefault) ?? folders[0])!.id
    }
  },
  { immediate: true },
)

async function addOne(id: number) {
  if (!selectedFolder.value) return
  const ok = await store.add(id, selectedFolder.value.path, monitorOnAdd.value)
  if (ok) toast.success('Imported', 'The book was added and its files moved into the library.')
  else toast.error('Not imported', store.matchState(id).error ?? 'The import failed.')
}

function openFix(id: number) {
  fixing.value = store.items.find((i) => i.id === id) ?? null
}

async function saveMatch(result: SearchResult, candidates: SearchResult[], andImport: boolean) {
  const item = fixing.value
  fixing.value = null
  if (!item) return
  store.selectMatch(item.id, result, candidates)
  if (andImport) await addOne(item.id)
}

async function importSelected() {
  if (!selectedFolder.value) return
  const ids = selectedAddable.value.map((i) => i.id)
  bulkBusy.value = true
  try {
    const result = await store.addMany(ids, selectedFolder.value.path, monitorOnAdd.value)
    if (result.failed === 0)
      toast.success('Imported', `${result.added} book${result.added === 1 ? '' : 's'} added.`)
    else
      toast.warning(
        'Partly imported',
        `${result.added} added, ${result.failed} failed. Each row says why.`,
      )
  } finally {
    bulkBusy.value = false
  }
}

async function ignoreSelected() {
  const ids = store.selectedItems.map((i) => i.id)
  bulkBusy.value = true
  try {
    const done = await store.ignoreMany(ids)
    store.clearSelection()
    toast.success('Ignored', `${done} book${done === 1 ? '' : 's'} hidden; files kept.`)
  } finally {
    bulkBusy.value = false
  }
}

async function discard(id: number) {
  const item = store.items.find((i) => i.id === id)
  if (!item) return
  const names = item.files.map((f) => folderName(f.path))
  const listed =
    names.slice(0, 8).join('\n') + (names.length > 8 ? `\n… and ${names.length - 8} more` : '')
  const yes = await showConfirm(
    `Delete ${item.files.length} file${item.files.length === 1 ? '' : 's'} from ${item.bookFolder}?\n\n${listed}\n\nThis cannot be undone. If a torrent client is still seeding these files, remove it there first.`,
    `Delete "${item.title ?? folderName(item.bookFolder)}" from disk`,
    { confirmText: 'Delete files', danger: true },
  )
  if (!yes) return
  const ok = await store.decide(id, 'discard')
  if (ok) toast.success('Deleted', 'The files were deleted.')
  else toast.error('Not deleted', store.matchState(id).error ?? 'Nothing was deleted.')
}

function timeAgo(isoString: string): string {
  const diff = Date.now() - new Date(isoString).getTime()
  const minutes = Math.floor(diff / 60000)
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes} minute${minutes === 1 ? '' : 's'} ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} hour${hours === 1 ? '' : 's'} ago`
  return `${Math.floor(hours / 24)}d ago`
}

onMounted(async () => {
  store.subscribe()
  await Promise.all([store.load(), store.loadWatchFolders(), rootFoldersStore.load()])
})

onBeforeUnmount(() => {
  store.unsubscribe()
})
</script>

<style scoped>
.ft {
  background: #121417;
  border: 1px solid rgba(255, 255, 255, 0.07);
  border-radius: 10px;
  overflow: hidden;
  font-family: 'Helvetica Neue', Helvetica, Arial, sans-serif;
}

.ft-top {
  padding: 16px 22px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.07);
  display: flex;
  align-items: center;
  gap: 14px;
  flex-wrap: wrap;
}

.ft-tabs {
  display: flex;
  gap: 2px;
  padding: 3px;
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
}

.ft-tab {
  display: flex;
  align-items: center;
  gap: 7px;
  padding: 6px 13px;
  border-radius: 6px;
  border: none;
  background: none;
  color: #8b939d;
  font-size: 13px;
  cursor: pointer;
}

.ft-tab.active {
  background: #1f2429;
  color: #e8eaed;
  font-weight: 500;
}

.ft-count {
  padding: 1px 6px;
  border-radius: 9px;
  background: #2a78d6;
  color: #fff;
  font:
    600 11px ui-monospace,
    Menlo,
    monospace;
}

.ft-controls {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.ft-label {
  font-size: 12.5px;
  color: #8b939d;
}

.ft-select {
  padding: 7px 11px;
  border-radius: 7px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  background: transparent;
  color: #c3cad2;
  font-size: 12.5px;
}

.ft-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 15px;
  border-radius: 7px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  background: none;
  color: #c3cad2;
  font-size: 13px;
  cursor: pointer;
}

.ft-btn.primary {
  background: #2a78d6;
  border-color: #2a78d6;
  color: #fff;
  font-weight: 500;
}

.ft-btn:disabled {
  opacity: 0.5;
  cursor: default;
}

.ft-watch {
  padding: 11px 22px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.07);
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12.5px;
  color: #79828c;
  flex-wrap: wrap;
}

.ft-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #5aa269;
}

.ft-dot.off {
  background: #e4b64a;
}

.ft-path {
  color: #c3cad2;
}

.ft-sep {
  opacity: 0.4;
}

.ft-warning {
  color: #e4b64a;
}

.ft-manage {
  margin-left: auto;
  color: #5aa2f5;
}

.ft-bulk {
  padding: 10px 22px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.07);
  background: rgba(42, 120, 214, 0.1);
  display: flex;
  align-items: center;
  gap: 14px;
  flex-wrap: wrap;
}

.ft-bulk-check {
  width: 16px;
  height: 16px;
  border-radius: 4px;
  background: #2a78d6;
  color: #fff;
  font:
    600 11px/16px 'Helvetica Neue',
    Helvetica,
    sans-serif;
  text-align: center;
}

.ft-bulk-count {
  font-weight: 500;
  font-size: 13px;
  color: #e8eaed;
}

.ft-link {
  background: none;
  border: none;
  padding: 0;
  color: #7fb8ff;
  font-size: 12.5px;
  cursor: pointer;
}

.ft-bulk-actions {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 9px;
}

.ft-head {
  display: grid;
  grid-template-columns: 34px minmax(260px, 2fr) minmax(190px, 1fr) minmax(220px, 1.2fr) 236px;
  gap: 0 16px;
  padding: 10px 22px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.07);
  font:
    400 10.5px ui-monospace,
    Menlo,
    monospace;
  color: #69727c;
  letter-spacing: 0.09em;
  text-transform: uppercase;
}

.ft-section {
  padding: 13px 22px;
  background: rgba(255, 255, 255, 0.02);
  border-bottom: 1px solid rgba(255, 255, 255, 0.07);
  display: flex;
  align-items: center;
  gap: 11px;
  flex-wrap: wrap;
}

.ft-section-title {
  font-weight: 600;
  font-size: 12.5px;
  color: #c3cad2;
}

.ft-section-count {
  padding: 2px 8px;
  border-radius: 9px;
  background: rgba(228, 182, 74, 0.14);
  color: #e4b64a;
  font:
    600 11px ui-monospace,
    Menlo,
    monospace;
}

.ft-section-note {
  font-size: 12.5px;
  color: #79828c;
}

.ft-section-toggle {
  margin-left: auto;
  color: #5aa2f5;
}

.ft-empty {
  padding: 1rem;
}

@media (max-width: 1100px) {
  .ft-head {
    display: none;
  }
}
</style>

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
  <div class="found-tab">
    <div class="found-toolbar">
      <div class="found-filters">
        <button
          v-for="f in filters"
          :key="f.id"
          class="filter"
          :class="{ active: store.filter === f.id }"
          @click="store.filter = f.id"
        >
          {{ f.label }}
          <Pill v-if="store.counts[f.id] > 0" variant="count" size="small">{{
            store.counts[f.id]
          }}</Pill>
        </button>
      </div>

      <div class="found-controls">
        <label class="control">
          <span>Library folder</span>
          <select v-model="selectedFolderId" class="folder-select">
            <option v-for="f in rootFoldersStore.folders" :key="f.id" :value="f.id">
              {{ f.name || f.path }}
            </option>
          </select>
        </label>
        <Checkbox v-model="monitorOnAdd">Monitor added books</Checkbox>
        <button
          class="btn btn-primary btn-sm"
          :disabled="!selectedFolder || store.addableItems.length === 0 || addingAll"
          :title="
            store.addableItems.length === 0 ? 'Nothing matched and complete to add' : undefined
          "
          @click="addAll"
        >
          <PhSpinner v-if="addingAll" class="ph-spin" :size="14" />
          <PhPlus v-else :size="14" />
          Add {{ store.addableItems.length }} matched
        </button>
        <button
          class="btn btn-secondary btn-sm"
          :disabled="store.scanning"
          title="Look through the watch folders now"
          @click="store.scan()"
        >
          <PhSpinner v-if="store.scanning" class="ph-spin" :size="14" />
          <PhMagnifyingGlass v-else :size="14" />
          {{ store.scanning ? 'Scanning…' : 'Scan now' }}
        </button>
      </div>
    </div>

    <p class="watch-line" v-if="store.watchFolders">
      <template v-if="store.watchFolders.folders.length > 0">
        Watching
        <span
          v-for="(f, i) in store.watchFolders.folders"
          :key="f.path"
          class="watch-path"
          :title="f.path"
          >{{ folderName(f.path)
          }}<template v-if="i < store.watchFolders.folders.length - 1">, </template></span
        >
        <template v-if="!store.watchFolders.fromSettings"> (from your download clients)</template>.
      </template>
      <template v-else
        >No watch folders: add one in Settings, or enable a download client with a download
        path.</template
      >
      <span v-if="store.lastScanCompletedAt" class="scan-meta">
        Last scan {{ timeAgo(store.lastScanCompletedAt) }}.</span
      >
      <span v-for="w in store.watchFolders.warnings" :key="w" class="watch-warning"> {{ w }}</span>
    </p>

    <LoadingState v-if="store.loading" message="Looking through the watch folders..." />
    <EmptyState v-else-if="store.error" title="Could not load found books" :message="store.error" />
    <EmptyState
      v-else-if="store.visibleItems.length === 0"
      :title="emptyTitle"
      :message="emptyMessage"
    />
    <div v-else class="table-wrap">
      <table class="found-table">
        <thead>
          <tr>
            <th>Book</th>
            <th>Files</th>
            <th>Complete</th>
            <th>Library</th>
            <th>Match</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <FoundBookRow
            v-for="item in store.visibleItems"
            :key="item.id"
            :item="item"
            :can-import="!!selectedFolder"
            @add="addOne"
            @discard="discard"
          />
        </tbody>
      </table>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { PhMagnifyingGlass, PhPlus, PhSpinner } from '@phosphor-icons/vue'
import { EmptyState, LoadingState, Pill } from '@/components/base'
import { Checkbox } from '@/components/form'
import FoundBookRow from '@/components/domain/audiobook/FoundBookRow.vue'
import { showConfirm } from '@/composables'
import { useToast } from '@/services/toastService'
import { folderName, useFoundBooksStore, type FoundBookFilter } from '@/stores/foundBooks'
import { useRootFoldersStore } from '@/stores/rootFolders'

const store = useFoundBooksStore()
const rootFoldersStore = useRootFoldersStore()
const toast = useToast()

const filters: { id: FoundBookFilter; label: string }[] = [
  { id: 'pending', label: 'Offered' },
  { id: 'blocked', label: 'Waiting' },
  { id: 'ignored', label: 'Ignored' },
  { id: 'done', label: 'Done' },
]

const selectedFolderId = ref<number | null>(null)
const selectedFolder = computed(
  () => rootFoldersStore.folders.find((f) => f.id === selectedFolderId.value) ?? null,
)
const monitorOnAdd = ref(true)
const addingAll = ref(false)

watch(
  () => rootFoldersStore.folders,
  (folders) => {
    if (selectedFolderId.value == null && folders.length > 0) {
      selectedFolderId.value = (folders.find((f) => f.isDefault) ?? folders[0])!.id
    }
  },
  { immediate: true },
)

const emptyTitle = computed(() => {
  switch (store.filter) {
    case 'pending':
      return 'Nothing found'
    case 'blocked':
      return 'Nothing waiting'
    case 'ignored':
      return 'Nothing ignored'
    default:
      return 'Nothing decided yet'
  }
})
const emptyMessage = computed(() => {
  switch (store.filter) {
    case 'pending':
      return 'Every complete book in the watch folders is either in the library or has been decided on. Scan now to look again.'
    case 'blocked':
      return 'Books still downloading, still settling, or still owned by a download appear here.'
    case 'ignored':
      return 'Books you chose to leave where they are appear here, and can be restored.'
    default:
      return 'Imported and discarded books stay here until the next scan confirms they are gone.'
  }
})

async function addOne(id: number) {
  if (!selectedFolder.value) return
  const ok = await store.add(id, selectedFolder.value.path, monitorOnAdd.value)
  if (ok) toast.success('Added', 'The book was added and its files moved into the library.')
  else toast.error('Not added', store.matchState(id).error ?? 'The import failed.')
}

async function addAll() {
  if (!selectedFolder.value) return
  const n = store.addableItems.length
  const yes = await showConfirm(
    `Add ${n} matched book${n === 1 ? '' : 's'} to the library and move their files in?`,
    'Add matched books',
    { confirmText: 'Add' },
  )
  if (!yes) return
  addingAll.value = true
  try {
    const result = await store.addAll(selectedFolder.value.path, monitorOnAdd.value)
    if (result.failed === 0)
      toast.success('Added', `${result.added} book${result.added === 1 ? '' : 's'} added.`)
    else
      toast.warning(
        'Partly added',
        `${result.added} added, ${result.failed} failed. Each row says why.`,
      )
  } finally {
    addingAll.value = false
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
    `Discard "${item.title ?? folderName(item.bookFolder)}"`,
    { confirmText: 'Delete files', danger: true },
  )
  if (!yes) return
  const ok = await store.decide(id, 'discard')
  if (ok) toast.success('Discarded', 'The files were deleted.')
  else toast.error('Not discarded', store.matchState(id).error ?? 'Nothing was deleted.')
}

function timeAgo(isoString: string): string {
  const diff = Date.now() - new Date(isoString).getTime()
  const minutes = Math.floor(diff / 60000)
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
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
.found-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
  margin-bottom: 0.75rem;
}

.found-filters {
  display: flex;
  gap: 0.25rem;
}

.filter {
  background: none;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 999px;
  color: #aaa;
  padding: 0.3rem 0.75rem;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.85rem;
}

.filter.active {
  color: white;
  border-color: var(--brand-500);
}

.found-controls {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.control {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.85rem;
  color: #aaa;
}

.folder-select {
  background: rgba(255, 255, 255, 0.06);
  color: inherit;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 6px;
  padding: 0.3rem 0.5rem;
}

.watch-line {
  color: #999;
  font-size: 0.85rem;
  margin: 0 0 1rem;
}

.watch-path {
  color: #ccc;
}

.watch-warning {
  display: block;
  color: var(--warning-400, #ffb74d);
}

.table-wrap {
  overflow-x: auto;
}

.found-table {
  width: 100%;
  border-collapse: collapse;
}

.found-table th {
  text-align: left;
  font-weight: 600;
  font-size: 0.8rem;
  color: #999;
  padding: 0.4rem 0.6rem;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
}
</style>

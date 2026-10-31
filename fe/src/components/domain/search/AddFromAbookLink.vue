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
  <section v-if="connected" class="abook-add">
    <button class="abook-toggle" type="button" :aria-expanded="open" @click="open = !open">
      {{ open ? '▾' : '▸' }} Add from abook.link
    </button>

    <div v-if="open" class="abook-body">
      <p class="abook-help">
        For a book Audible has no record of. Paste the post's link or topic number: the book is
        added from the post's own title and author, and that post is downloaded for it.
      </p>
      <form class="abook-row" @submit.prevent="preview">
        <input
          v-model="input"
          class="form-input abook-input"
          type="text"
          placeholder="https://abook.link/book/index.php?topic=71604"
          aria-label="abook.link post link or topic number"
        />
        <button class="btn btn-secondary" type="submit" :disabled="loading || !input.trim()">
          {{ loading ? 'Reading…' : 'Preview' }}
        </button>
      </form>

      <p v-if="error" class="abook-error">{{ error }}</p>

      <div v-if="topic" class="abook-preview">
        <div class="abook-title">{{ topic.title || topic.topicTitle }}</div>
        <div class="abook-meta">
          <span v-if="topic.author">{{ topic.author }}</span>
          <span v-if="topic.narrator"> · read by {{ topic.narrator }}</span>
          <span v-if="topic.seriesName"> · {{ topic.seriesName }} {{ topic.seriesPosition }}</span>
          <span v-if="topic.year"> · {{ topic.year }}</span>
          <span v-if="topic.format"> · {{ topic.format }}</span>
          <span v-if="topic.sizeBytes"> · {{ formatSize(topic.sizeBytes) }}</span>
        </div>
        <button class="btn btn-primary" type="button" :disabled="sending" @click="openAdd">
          {{ sending ? 'Sending…' : 'Add and download' }}
        </button>
      </div>
    </div>

    <AddLibraryModal
      v-if="pendingBook"
      :visible="true"
      :book="pendingBook"
      :auto-search-default="false"
      @close="pendingBook = null"
      @added="sendFor"
    />
  </section>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import AddLibraryModal from '@/components/domain/audiobook/AddLibraryModal.vue'
import { apiService } from '@/services/api'
import { useToast } from '@/services/toastService'
import { describeApiError } from '@/utils/apiError'
import type { AbookTopic, Audiobook, AudibleBookMetadata } from '@/types'

const toast = useToast()

// Shown only when abook.link is set up as an enabled indexer: without it there is no
// account to read posts with, and no way to resolve one to a download.
const connected = ref(false)
const open = ref(false)
const input = ref('')
const loading = ref(false)
const sending = ref(false)
const error = ref<string | null>(null)
const topic = ref<AbookTopic | null>(null)
const pendingBook = ref<AudibleBookMetadata | null>(null)

onMounted(async () => {
  try {
    const indexers = await apiService.getIndexers()
    connected.value = indexers.some(
      (indexer) => indexer.isEnabled && indexer.implementation?.toLowerCase() === 'abooklink',
    )
  } catch {
    connected.value = false
  }
})

/** A topic number, from a full post link or the bare number. */
function parseTopicId(value: string): number | null {
  const trimmed = value.trim()
  const fromLink = /[?&]topic=(\d+)/.exec(trimmed)
  const digits = fromLink?.[1] ?? (/^\d+$/.test(trimmed) ? trimmed : null)
  const id = digits ? Number(digits) : NaN
  return Number.isInteger(id) && id > 0 ? id : null
}

async function preview() {
  error.value = null
  topic.value = null
  const id = parseTopicId(input.value)
  if (id === null) {
    error.value = 'That is not an abook.link post link or topic number.'
    return
  }

  loading.value = true
  try {
    const result = await apiService.getAbookTopic(id)
    if (result.outcome === 'NotARelease' || result.outcome === 'ArchiveSpot') {
      error.value = 'That topic is not a release that can be downloaded.'
      return
    }
    topic.value = result
  } catch (e) {
    error.value = describeApiError(e, 'The post could not be read.')
  } finally {
    loading.value = false
  }
}

function splitNames(value?: string): string[] {
  return (value ?? '')
    .split(/,|\s&\s|\sand\s/)
    .map((name) => name.trim())
    .filter(Boolean)
}

/** Opens the add form seeded from the post, editable before anything is saved. */
function openAdd() {
  const post = topic.value
  if (!post) return
  pendingBook.value = {
    asin: post.asin ?? '',
    title: post.title || post.topicTitle,
    authors: splitNames(post.author),
    narrators: splitNames(post.narrator),
    series: post.seriesName,
    seriesNumber: post.seriesPosition,
    publishYear: post.year ? String(post.year) : undefined,
    genres: post.genre ? [post.genre] : [],
    source: 'abook.link',
    metadataSource: 'abook.link',
    sourceLink: `https://abook.link/book/index.php?topic=${post.topicId}`,
  }
}

/** Once the book exists, sends this exact post as its download. */
async function sendFor(audiobook: Audiobook) {
  pendingBook.value = null
  const post = topic.value
  if (!post || !audiobook?.id) return

  sending.value = true
  try {
    await apiService.sendAbookTopic(post.topicId, audiobook.id)
    toast.success('Downloading', `"${audiobook.title}" was sent to the download client.`)
    topic.value = null
    input.value = ''
  } catch (e) {
    toast.error(
      'Added, but not downloading',
      describeApiError(e, 'The post could not be sent to the download client.'),
    )
  } finally {
    sending.value = false
  }
}

function formatSize(bytes: number): string {
  const mb = bytes / (1024 * 1024)
  return mb >= 1024 ? `${(mb / 1024).toFixed(1)} GB` : `${Math.round(mb)} MB`
}
</script>

<style scoped>
.abook-add {
  margin-bottom: 1.5rem;
}

.abook-toggle {
  background: none;
  border: none;
  color: var(--text-secondary);
  cursor: pointer;
  font-size: 0.9rem;
  padding: 0;
}

.abook-body {
  margin-top: 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.abook-help {
  margin: 0;
  color: var(--text-muted);
  font-size: 0.85rem;
}

.abook-row {
  display: flex;
  gap: 0.5rem;
}

.abook-input {
  flex: 1;
  min-width: 0;
}

.abook-error {
  margin: 0;
  color: var(--danger-500, #e5534b);
  font-size: 0.85rem;
}

.abook-preview {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  align-items: flex-start;
}

.abook-title {
  font-weight: 600;
}

.abook-meta {
  color: var(--text-secondary);
  font-size: 0.85rem;
}
</style>

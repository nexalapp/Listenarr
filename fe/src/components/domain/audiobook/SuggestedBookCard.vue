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
  <div class="suggested-card">
    <div class="cover">
      <img
        :src="getProtectedImageSrc(book.imageUrl, getPlaceholderUrl())"
        :alt="`${book.title} cover`"
        loading="lazy"
      />
      <span v-if="showPosition && book.seriesNumber" class="position"
        >#{{ book.seriesNumber }}</span
      >
    </div>
    <div class="body">
      <div class="title" :title="book.title">{{ book.title }}</div>
      <div class="meta">
        <span v-if="authorLine" :title="book.authors.join(', ')">{{ authorLine }}</span>
        <span v-if="year"> · {{ year }}</span>
        <span v-if="book.runtime"> · {{ formatRuntime(book.runtime) }}</span>
      </div>
    </div>
    <div class="actions">
      <button class="btn btn-primary btn-sm" title="Add to library" @click="emit('add')">
        <PhPlus /> Add
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { PhPlus } from '@phosphor-icons/vue'
import { useProtectedImages } from '@/composables/useProtectedImages'
import { getPlaceholderUrl } from '@/utils/placeholder'
import { formatRuntime } from '@/utils/searchResultFormatting'
import type { SuggestedBook } from '@/types'

const props = defineProps<{
  book: SuggestedBook
  showPosition?: boolean
}>()

const emit = defineEmits<{
  add: []
}>()

const { getProtectedImageSrc } = useProtectedImages()

const year = computed(() => props.book.publishedDate?.match(/\d{4}/)?.[0])

// Anthologies credit a dozen names; two is enough to place the book.
const authorLine = computed(() => {
  const names = props.book.authors
  if (names.length <= 2) return names.join(', ')
  return `${names.slice(0, 2).join(', ')} +${names.length - 2}`
})
</script>

<style scoped>
.suggested-card {
  display: flex;
  flex-direction: column;
  background: rgba(255, 255, 255, 0.04);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 8px;
  overflow: hidden;
}

.cover {
  position: relative;
  aspect-ratio: 1;
  background: #222;
}

.cover img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.position {
  position: absolute;
  top: 0.4rem;
  left: 0.4rem;
  background: rgba(0, 0, 0, 0.75);
  color: white;
  font-size: 0.7rem;
  padding: 0.1rem 0.4rem;
  border-radius: 4px;
}

.body {
  padding: 0.6rem 0.6rem 0.3rem;
  flex: 1;
}

.title {
  color: white;
  font-weight: 500;
  font-size: 0.9rem;
  line-height: 1.25;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.meta {
  color: #999;
  font-size: 0.75rem;
  margin-top: 0.25rem;
}

.actions {
  display: flex;
  gap: 0.4rem;
  padding: 0.4rem 0.6rem 0.6rem;
}

.actions .btn-primary {
  flex: 1;
}

.btn-sm {
  padding: 0.3rem 0.5rem;
  font-size: 0.8rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.3rem;
}
</style>

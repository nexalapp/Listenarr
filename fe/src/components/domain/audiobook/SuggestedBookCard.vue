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
  <article class="suggested-card" :class="{ added }">
    <img
      class="cover"
      :src="getProtectedImageSrc(book.imageUrl, getPlaceholderUrl())"
      :alt="`${book.title} cover`"
      loading="lazy"
    />
    <div class="body">
      <div class="head">
        <h3 class="title" :title="book.title">
          <span v-if="showPosition && book.seriesNumber" class="position"
            >#{{ book.seriesNumber }}</span
          >
          {{ book.title }}
        </h3>
        <button
          v-if="!added"
          class="ignore"
          title="Not interested - stop suggesting this"
          aria-label="Ignore this suggestion"
          @click="emit('ignore')"
        >
          <PhX />
        </button>
        <span v-if="added" class="added-badge"><PhCheck weight="bold" /> Added</span>
        <button
          v-else
          class="btn btn-primary btn-sm add"
          title="Add to library"
          @click="emit('add')"
        >
          <PhPlus weight="bold" /> Add
        </button>
      </div>

      <div class="facts">
        <span v-if="rating != null" class="stars" :title="ratingTitle" aria-hidden="true">
          <span class="stars-lit" :style="{ width: `${(rating / 5) * 100}%` }">★★★★★</span>
          <span class="stars-dim">★★★★★</span>
        </span>
        <span v-if="rating != null" class="rating-value">{{ rating.toFixed(1) }}</span>
        <span v-if="book.ratingCount" class="rating-count">{{
          compactCount(book.ratingCount)
        }}</span>
        <span v-if="rating != null && facts" class="dot">·</span>
        <span v-if="facts">{{ facts }}</span>
      </div>

      <p v-if="description" class="description" :class="{ clamped: !expanded }">
        {{ description }}
      </p>
      <button v-if="description.length > 220" class="link-btn" @click="expanded = !expanded">
        {{ expanded ? 'Show less' : 'Show more' }}
      </button>
    </div>
  </article>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { PhCheck, PhPlus, PhX } from '@phosphor-icons/vue'
import { useProtectedImages } from '@/composables/useProtectedImages'
import { getPlaceholderUrl } from '@/utils/placeholder'
import { formatRuntime } from '@/utils/searchResultFormatting'
import type { SuggestedBook } from '@/types'

const props = defineProps<{
  book: SuggestedBook
  showPosition?: boolean
  /** Added from this page during this visit: keep the card, swap the button. */
  added?: boolean
}>()

const emit = defineEmits<{
  add: []
  ignore: []
}>()

const { getProtectedImageSrc } = useProtectedImages()

const expanded = ref(false)

const rating = computed(() => props.book.ratingOverall ?? null)

// Overall is the number people know from Audible's page; story and performance
// stay in the tooltip for anyone choosing between narrations.
const ratingTitle = computed(() => {
  const parts = [`Overall ${rating.value?.toFixed(1)}`]
  if (props.book.ratingStory != null) parts.push(`Story ${props.book.ratingStory.toFixed(1)}`)
  if (props.book.ratingCount) parts.push(`${props.book.ratingCount.toLocaleString()} ratings`)
  return parts.join(' · ')
})

// Audible's summary is HTML; the card wants its text, paragraphs as breaks.
const description = computed(() =>
  (props.book.description ?? '')
    .replace(/<\/(p|div|br)\s*>|<br\s*\/?>/gi, '\n')
    .replace(/<[^>]+>/g, '')
    .replace(/&amp;/g, '&')
    .replace(/&quot;/g, '"')
    .replace(/&#39;|&apos;/g, "'")
    .replace(/&nbsp;/g, ' ')
    .replace(/[ \t]+\n/g, '\n')
    .replace(/\n{2,}/g, '\n')
    .trim(),
)

const facts = computed(() => {
  const year = props.book.publishedDate?.match(/\d{4}/)?.[0]
  return [year, props.book.runtime ? formatRuntime(props.book.runtime) : null]
    .filter(Boolean)
    .join(' · ')
})

function compactCount(count: number): string {
  return count >= 1000 ? `${(count / 1000).toFixed(count >= 10000 ? 0 : 1)}k` : String(count)
}
</script>

<style scoped>
.suggested-card {
  display: flex;
  gap: 0.9rem;
  padding: 0.9rem;
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.025);
  border: 1px solid rgba(255, 255, 255, 0.07);
  min-width: 0;
}

.cover {
  width: 132px;
  height: 132px;
  flex: none;
  border-radius: 6px;
  object-fit: cover;
  background: #23272e;
  /* The cover stays put while the description grows below it. */
  align-self: flex-start;
}

.body {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.head {
  display: flex;
  align-items: flex-start;
  gap: 0.6rem;
}

.title {
  flex: 1;
  min-width: 0;
  margin: 0;
  font-size: 0.97rem;
  font-weight: 600;
  line-height: 1.3;
  color: #e8eaed;
}

.position {
  color: #79828c;
  font-weight: 500;
  margin-right: 0.2rem;
}

/* The global .btn is sized for modal footers; this one sits beside a title. */
.suggested-card .add {
  flex: none;
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  padding: 0.35rem 0.7rem;
  min-width: 0;
  min-height: 0;
  height: auto;
  font-size: 0.8rem;
  line-height: 1.2;
  border-radius: 6px;
}

.ignore {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 1.75rem;
  height: 1.75rem;
  padding: 0;
  border-radius: 6px;
  border: 1px solid transparent;
  background: none;
  color: #6b747d;
  cursor: pointer;
}

.ignore:hover {
  color: #e8eaed;
  border-color: rgba(255, 255, 255, 0.15);
  background: rgba(255, 255, 255, 0.05);
}

.added-badge {
  flex: none;
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  padding: 0.35rem 0.75rem;
  border-radius: 6px;
  font-size: 0.8rem;
  font-weight: 500;
  background: rgba(76, 140, 90, 0.18);
  border: 1px solid rgba(96, 170, 112, 0.4);
  color: #8fd39f;
}

.facts {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-family: ui-monospace, Menlo, monospace;
  font-size: 0.75rem;
  color: #79828c;
}

.stars {
  position: relative;
  display: inline-block;
  letter-spacing: 1px;
  line-height: 1;
  color: #3a3f46;
}

.stars-lit {
  position: absolute;
  inset: 0 auto 0 0;
  overflow: hidden;
  white-space: nowrap;
  color: #e4b64a;
}

.rating-value {
  color: #c3cad2;
}

.rating-count::before {
  content: '(';
}

.rating-count::after {
  content: ')';
}

.dot {
  opacity: 0.4;
}

.description {
  margin: 0;
  font-size: 0.82rem;
  line-height: 1.55;
  color: #9aa3ad;
  text-wrap: pretty;
  white-space: pre-line;
}

.description.clamped {
  display: -webkit-box;
  -webkit-line-clamp: 3;
  line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.link-btn {
  align-self: flex-start;
  background: none;
  border: none;
  padding: 0;
  color: #5aa2f5;
  font-size: 0.8rem;
  cursor: pointer;
}

.link-btn:hover {
  color: #7fb8ff;
}

@media (max-width: 520px) {
  .cover {
    width: 96px;
    height: 96px;
  }
}
</style>

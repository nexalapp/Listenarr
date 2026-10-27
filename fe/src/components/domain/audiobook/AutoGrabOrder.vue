<!--
  Listenarr - Audiobook Management System
  Copyright (C) 2024-2026 Listenarr Contributors

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU Affero General Public License as published
  by the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.
-->
<!--
  The followed lists, in the order a run spends its allowance on them.

  Lifted out of the auto-grab panel so the tab that arms a list can show the same
  control rather than linking away to it. One implementation, two places: the tick
  and the order are halves of one decision and were in rooms that could not see
  each other.
-->
<template>
  <div class="autograb-order">
    <p v-if="hint" class="autograb-hint">
      The order decides whose books a run's allowance is spent on. Drag is overkill for five
      rows, so use the arrows.
    </p>

    <ul class="autograb-list">
      <li v-for="(source, index) in rows" :key="source.key">
        <input
          type="checkbox"
          :checked="source.enabled"
          :aria-label="`Include ${source.label}`"
          @change="store.toggle(source.kind, source.sectionId)"
        />
        <span class="autograb-label">{{ source.label }}</span>
        <span v-if="source.count != null" class="autograb-count">{{ source.count }} ready</span>
        <button
          type="button"
          class="autograb-move"
          :disabled="index === 0"
          aria-label="Move up"
          @click="move(index, -1)"
        >
          ↑
        </button>
        <button
          type="button"
          class="autograb-move"
          :disabled="index === rows.length - 1"
          aria-label="Move down"
          @click="move(index, 1)"
        >
          ↓
        </button>
      </li>
      <li v-if="rows.length === 0" class="autograb-empty">
        No list is ticked. Tick one here, or on the list itself.
      </li>
    </ul>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useAutoGrabStore } from '@/stores/autoGrab'
import type { AutoGrabKind } from '@/types'

const props = defineProps<{
  /**
   * How to name a followed list, keyed "kind" or "kind:sectionId". A chart category or
   * board names itself in the preview, which is where its name is known.
   */
  labels?: Record<string, string>
  /** The explanatory line; off where the surrounding page already says it. */
  hint?: boolean
}>()

const store = useAutoGrabStore()

function planned(key: string) {
  return store.plan?.bySource.find(
    (count) => (count.sectionId ? `${count.kind}:${count.sectionId}` : count.kind) === key,
  )
}

const rows = computed(() =>
  store.sources.map((source) => {
    const key = source.sectionId ? `${source.kind}:${source.sectionId}` : source.kind
    return {
      key,
      kind: source.kind as AutoGrabKind,
      sectionId: source.sectionId ?? null,
      enabled: source.enabled,
      label: planned(key)?.label ?? props.labels?.[key] ?? key,
      count: planned(key)?.available,
    }
  }),
)

async function move(index: number, by: number) {
  const current = store.settings ?? { enabled: false, perRun: 10, intervalMinutes: 60, sources: [] }
  const sources = [...current.sources]
  const target = index + by
  if (target < 0 || target >= sources.length) return
  const [moved] = sources.splice(index, 1)
  sources.splice(target, 0, moved!)
  await store.save({ ...current, sources })
}
</script>

<style scoped>
.autograb-hint {
  margin: 0 0 0.5rem;
  font-size: 0.82rem;
  opacity: 0.7;
}

.autograb-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.autograb-list li {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.9rem;
}

.autograb-label {
  flex: 1;
}

.autograb-count {
  font-size: 0.8rem;
  opacity: 0.65;
}

.autograb-move {
  background: none;
  border: 1px solid rgba(128, 128, 128, 0.4);
  border-radius: 4px;
  color: inherit;
  width: 1.5rem;
  height: 1.5rem;
  line-height: 1;
  cursor: pointer;
}

.autograb-move:disabled {
  opacity: 0.3;
  cursor: default;
}

.autograb-empty {
  opacity: 0.6;
  font-size: 0.85rem;
}
</style>

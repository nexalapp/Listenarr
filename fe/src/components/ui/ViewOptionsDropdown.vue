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
  <div class="view-options-dropdown" ref="root">
    <button
      type="button"
      class="trigger"
      :class="{ active: active }"
      @click="toggle"
      :aria-expanded="open ? 'true' : 'false'"
      aria-haspopup="true"
      :title="summary"
    >
      <span class="trigger-label">View</span>
      <PhSlidersHorizontal class="trigger-icon" />
    </button>

    <div v-if="open" class="dropdown">
      <div class="section-label">Sort by</div>
      <button
        v-for="opt in options"
        :key="opt.value"
        type="button"
        class="dropdown-item"
        :class="{ active: opt.value === currentValue }"
        role="menuitemradio"
        :aria-checked="opt.value === currentValue"
        @click="selectSort(opt.value)"
      >
        <span class="item-label">{{ opt.label }}</span>
        <component
          v-if="opt.value === currentValue"
          :is="directionIndicator"
          class="direction-indicator"
        />
      </button>

      <template v-if="groupingOptions.length > 0">
        <div class="dropdown-divider"></div>
        <div class="section-label">Group by</div>
        <button
          v-if="groupingOptions.includes('author')"
          type="button"
          class="dropdown-item"
          :class="{ active: groupByAuthor }"
          role="menuitemcheckbox"
          :aria-checked="groupByAuthor"
          @click="emit('update:groupByAuthor', !groupByAuthor)"
        >
          <span class="item-label">Author</span>
          <span v-if="groupByAuthor" class="check">✓</span>
        </button>
        <button
          v-if="groupingOptions.includes('series')"
          type="button"
          class="dropdown-item"
          :class="{ active: groupBySeries }"
          role="menuitemcheckbox"
          :aria-checked="groupBySeries"
          @click="emit('update:groupBySeries', !groupBySeries)"
        >
          <span class="item-label">Series</span>
          <span class="item-hint" v-if="groupByAuthor && groupBySeries">within author</span>
          <span v-if="groupBySeries" class="check">✓</span>
        </button>
      </template>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { PhSlidersHorizontal, PhArrowUp, PhArrowDown } from '@phosphor-icons/vue'

const props = withDefaults(
  defineProps<{
    modelValue?: string | null
    options?: Array<{ value: string; label: string }>
    sortOrder?: 'asc' | 'desc'
    currentValue?: string | null
    active?: boolean
    /** Which groupings this view offers; empty hides the section entirely. */
    groupingOptions?: Array<'author' | 'series'>
    groupByAuthor?: boolean
    groupBySeries?: boolean
  }>(),
  {
    modelValue: null,
    options: () => [],
    sortOrder: 'asc',
    currentValue: null,
    active: false,
    groupingOptions: () => [],
    groupByAuthor: false,
    groupBySeries: false,
  },
)

const emit = defineEmits<{
  (e: 'update:modelValue', v: string | null): void
  (e: 'update:groupByAuthor', v: boolean): void
  (e: 'update:groupBySeries', v: boolean): void
}>()

const open = ref(false)
const root = ref<HTMLElement | null>(null)

const options = computed(() => props.options || [])
const groupingOptions = computed(() => props.groupingOptions || [])

const selectedLabel = computed(() => {
  const found = options.value.find((o) => o.value === props.modelValue)
  return found ? found.label : (options.value[0]?.label ?? '')
})

// The trigger only says "View", so the tooltip carries what is actually in effect.
const summary = computed(() => {
  const parts = [`Sort: ${selectedLabel.value} ${props.sortOrder === 'asc' ? '(A–Z)' : '(Z–A)'}`]
  if (groupingOptions.value.length > 0) {
    const groups = [
      groupingOptions.value.includes('author') && props.groupByAuthor ? 'author' : '',
      groupingOptions.value.includes('series') && props.groupBySeries ? 'series' : '',
    ].filter(Boolean)
    parts.push(groups.length ? `Grouped by ${groups.join(', then ')}` : 'No grouping')
  }
  return parts.join(' · ')
})

const directionIndicator = computed(() => (props.sortOrder === 'asc' ? PhArrowDown : PhArrowUp))

function toggle() {
  open.value = !open.value
}

function close() {
  open.value = false
}

// The panel stays open on every click: picking the same sort key again flips its
// direction, and the two grouping toggles are meant to be combined.
function selectSort(v: string) {
  emit('update:modelValue', v)
}

function handleClickOutside(e: MouseEvent) {
  if (!root.value) return
  if (!root.value.contains(e.target as Node)) close()
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') close()
}

onMounted(() => {
  document.addEventListener('click', handleClickOutside)
  document.addEventListener('keydown', handleKeydown)
})
onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside)
  document.removeEventListener('keydown', handleKeydown)
})
</script>

<style scoped>
.view-options-dropdown {
  position: relative;
  display: inline-block;
}
.trigger {
  background: #2a2a2a;
  color: #e6eef8;
  border: 1px solid rgba(255, 255, 255, 0.06);
  padding: 8px 12px;
  border-radius: 6px;
  display: inline-flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  font-size: 12px;
}
.trigger.active {
  background-color: var(--brand-500);
  border-color: var(--brand-500);
  color: #fff;
}
.trigger-icon {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
}
.dropdown {
  position: absolute;
  top: calc(100% + 6px);
  left: auto;
  right: 0;
  min-width: 220px;
  background: #2a2a2a;
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.6);
  z-index: 1100;
  padding: 6px 0;
  max-height: 60vh;
  overflow-y: auto;
}
.section-label {
  padding: 6px 1rem;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: #8b98a5;
}
.dropdown-item {
  width: 100%;
  background: none;
  border: none;
  text-align: left;
  padding: 0.6rem 1rem;
  cursor: pointer;
  color: #ddd;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  font-size: 12px;
  transition: background-color 0.15s;
}
.dropdown-item:hover {
  background-color: rgba(255, 255, 255, 0.18);
  color: #fff;
}
.dropdown-item.active {
  background-color: rgba(33, 150, 243, 0.1);
  color: #fff;
}
.item-label {
  flex: 1;
}
.item-hint {
  font-size: 11px;
  color: #8b98a5;
}
.dropdown-divider {
  height: 1px;
  background: rgba(255, 255, 255, 0.06);
  margin: 6px 0;
}
.check {
  color: #4dabf7;
}
.direction-indicator {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
  color: #2196f3;
}

@media (max-width: 1024px) {
  .dropdown {
    min-width: 200px;
    max-width: calc(100vw - 16px);
  }
  .trigger {
    padding: 8px 6px;
    min-width: 36px;
    justify-content: center;
    gap: 0;
  }
  .trigger-label {
    display: none;
  }
  .trigger-icon {
    width: 18px;
    height: 18px;
  }
}
</style>

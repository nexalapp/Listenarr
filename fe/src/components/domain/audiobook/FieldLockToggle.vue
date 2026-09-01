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
  <button
    type="button"
    class="field-lock"
    :class="{ 'field-lock--on': locked }"
    :aria-pressed="locked"
    :aria-label="label"
    :title="label"
    @click="toggle"
  >
    <PhLock v-if="locked" :size="13" weight="fill" />
    <PhLockOpen v-else :size="13" />
  </button>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { PhLock, PhLockOpen } from '@phosphor-icons/vue'
import type { LockableField } from '@/types'

const props = defineProps<{
  field: LockableField
  /** The whole book's lock set. Held by the form, so one save carries all of it. */
  modelValue: LockableField[]
  /** What this field is called, for the button's label. */
  name: string
}>()

const emit = defineEmits<{
  (event: 'update:modelValue', value: LockableField[]): void
}>()

const locked = computed(() => props.modelValue.includes(props.field))

const label = computed(() =>
  locked.value
    ? `${props.name} is pinned — a metadata rescan will not change it. Click to unpin.`
    : `Pin ${props.name} so a metadata rescan cannot change it.`,
)

function toggle() {
  emit(
    'update:modelValue',
    locked.value
      ? props.modelValue.filter((value) => value !== props.field)
      : [...props.modelValue, props.field],
  )
}
</script>

<style scoped>
.field-lock {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  padding: 0;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-disabled);
  cursor: pointer;
  transition: var(--transition-fast);
}

.field-lock:hover {
  background: var(--bg-tertiary);
  color: var(--text-secondary);
}

.field-lock--on {
  color: var(--warning-500);
}

.field-lock--on:hover {
  color: var(--warning-600);
}

.field-lock:focus-visible {
  outline: 2px solid var(--brand-focus);
  outline-offset: 1px;
}
</style>

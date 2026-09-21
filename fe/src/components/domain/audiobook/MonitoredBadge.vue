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
    v-if="inLibrary"
    type="button"
    class="monitored-badge toggleable"
    :class="{ unmonitored: !monitored }"
    :title="monitored ? 'Monitored - click to stop monitoring' : 'Unmonitored - click to monitor'"
    :aria-pressed="monitored"
    :disabled="busy"
    @click.stop="emit('toggle')"
  >
    <component :is="monitored ? PhToggleRight : PhToggleLeft" weight="fill" />
    {{ monitored ? 'Monitored' : 'Unmonitored' }}
  </button>
  <div v-else class="monitored-badge unmonitored">
    <PhEyeSlash />
    Not Added
  </div>
</template>

<script setup lang="ts">
/**
 * One badge for a book's monitoring, everywhere a book appears in a list.
 *
 * For a library book it is the switch: the icon is a toggle, not an eye, so it
 * reads as something to click before anyone hovers. A book not in the library
 * has nothing to switch and says so.
 */
import { PhEyeSlash, PhToggleLeft, PhToggleRight } from '@phosphor-icons/vue'

defineProps<{
  monitored: boolean
  inLibrary: boolean
  busy?: boolean
}>()

const emit = defineEmits<{ toggle: [] }>()
</script>

<style scoped>
.monitored-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  margin-top: 0.5rem;
  padding: 0.25rem 0.5rem;
  margin-left: 0.25rem;
  background-color: rgba(46, 204, 113, 0.2);
  border: 1px solid rgba(46, 204, 113, 0.4);
  border-radius: 6px;
  font: inherit;
  font-size: 10px;
  font-weight: 500;
  color: #2ecc71;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 100%;
  line-height: 1.2;
}

.monitored-badge svg {
  font-size: 14px;
  flex-shrink: 0;
}

.monitored-badge.unmonitored {
  /* Neutral, not red: an unmonitored book is a state, not a destructive action.
     Red is reserved for delete so the two do not read as the same severity. */
  background-color: rgba(148, 163, 184, 0.15);
  border-color: rgba(148, 163, 184, 0.35);
  color: var(--text-muted);
}

.toggleable {
  cursor: pointer;
  /* Above the row's click overlay and the cover's hover overlay. */
  position: relative;
  z-index: 30;
  pointer-events: auto;
  transition:
    background-color 0.15s,
    border-color 0.15s,
    color 0.15s;
}

.toggleable:hover:not(:disabled) {
  background-color: rgba(46, 204, 113, 0.32);
  border-color: rgba(46, 204, 113, 0.65);
}

.toggleable.unmonitored:hover:not(:disabled) {
  background-color: rgba(148, 163, 184, 0.28);
  border-color: rgba(148, 163, 184, 0.6);
  color: #e6eef8;
}

.toggleable:disabled {
  cursor: progress;
  opacity: 0.7;
}

.toggleable:focus-visible {
  outline: 2px solid rgba(var(--brand-rgb), 0.5);
  outline-offset: 1px;
}
</style>

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
  <section class="settings-page-shell">
    <header class="settings-page-header">
      <div class="settings-page-heading">
        <h1><component :is="icon" v-if="icon" /> {{ title }}</h1>
        <p v-if="description" class="settings-page-description">{{ description }}</p>
      </div>
      <div class="settings-page-actions">
        <slot name="actions" />
        <button
          v-if="showSave"
          type="button"
          class="btn btn-primary"
          :disabled="saving || saveDisabled"
          :title="saveTitle"
          @click="emit('save')"
        >
          <PhSpinner v-if="saving" class="ph-spin" />
          <PhFloppyDisk v-else />
          {{ saving ? 'Saving...' : 'Save' }}
        </button>
      </div>
    </header>
    <div class="settings-page-body">
      <slot />
    </div>
  </section>
</template>

<script setup lang="ts">
import type { Component } from 'vue'
import { PhFloppyDisk, PhSpinner } from '@phosphor-icons/vue'

withDefaults(
  defineProps<{
    title: string
    description?: string
    icon?: Component
    showSave?: boolean
    saving?: boolean
    saveDisabled?: boolean
    saveTitle?: string
  }>(),
  {
    description: undefined,
    icon: undefined,
    showSave: false,
    saving: false,
    saveDisabled: false,
    saveTitle: undefined,
  },
)
const emit = defineEmits<{ save: [] }>()
</script>

<style scoped>
.settings-page-shell {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.settings-page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
  flex-wrap: wrap;
  padding-bottom: 1rem;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.settings-page-heading h1 {
  margin: 0;
  color: white;
  font-size: 1.6rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.settings-page-description {
  margin: 0.35rem 0 0;
  color: #999;
  font-size: 0.9rem;
  max-width: 60ch;
}

.settings-page-actions {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  flex-wrap: wrap;
}

.settings-page-actions .btn {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
}

.settings-page-body {
  display: flex;
  flex-direction: column;
  gap: 2rem;
}
</style>

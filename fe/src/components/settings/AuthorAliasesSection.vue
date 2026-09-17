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
  <div class="form-section">
    <h3><PhUsers /> Author Names</h3>
    <div class="form-body">
      <FormRow
        label="Author Aliases"
        help="Spellings a provider may send, and the one the library keeps. Audible credits the same author as 'B. V. Larson' on one title and 'B.V. Larson' on the next, and the library groups by the exact string. Each alias is applied whenever a book is saved — a match, a refresh, an import or an edit — so the variant never reaches the shelf. Nothing is inferred; only the spellings listed here are changed."
      >
        <div class="alias-list">
          <div v-for="(alias, index) in aliases" :key="index" class="alias-row">
            <input
              v-model="alias.variant"
              type="text"
              placeholder="Spelling to replace (e.g. B.V. Larson)"
              @change="commit"
            />
            <span class="alias-arrow">→</span>
            <input
              v-model="alias.canonical"
              type="text"
              placeholder="Spelling to keep (e.g. B. V. Larson)"
              @change="commit"
            />
            <button
              type="button"
              class="alias-remove"
              title="Remove this alias"
              @click="remove(index)"
            >
              <PhX :size="14" />
            </button>
          </div>
          <div class="alias-actions">
            <button type="button" class="btn btn-secondary btn-sm" @click="add">
              <PhPlus :size="14" /> Add alias
            </button>
            <button
              type="button"
              class="btn btn-secondary btn-sm"
              :disabled="applying || aliases.length === 0"
              title="Rewrite the author names already stored in the library through these aliases. Save settings first."
              @click="applyToLibrary"
            >
              <PhArrowsClockwise :size="14" :class="{ 'ph-spin': applying }" /> Apply to library
            </button>
            <span v-if="applyMessage" class="alias-note">{{ applyMessage }}</span>
          </div>
        </div>
      </FormRow>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { PhArrowsClockwise, PhPlus, PhUsers, PhX } from '@phosphor-icons/vue'
import FormRow from '@/components/settings/FormRow.vue'
import { apiService } from '@/services/api'
import { logger } from '@/utils/logger'
import type { ApplicationSettings } from '@/types'

const props = defineProps<{ settings: Partial<ApplicationSettings> }>()
const emit = defineEmits<{
  'update:settings': [value: Partial<ApplicationSettings>]
}>()

interface AliasRow {
  variant: string
  canonical: string
}

function parse(json: string | undefined): AliasRow[] {
  if (!json) return []
  try {
    const parsed = JSON.parse(json)
    if (!Array.isArray(parsed)) return []
    return parsed
      .filter((entry) => entry && typeof entry.variant === 'string')
      .map((entry) => ({ variant: entry.variant ?? '', canonical: entry.canonical ?? '' }))
  } catch {
    return []
  }
}

const aliases = ref<AliasRow[]>(parse(props.settings.authorAliasesJson))
const applying = ref(false)
const applyMessage = ref<string | null>(null)

// The stored JSON is the source of truth; a reload or a save from elsewhere replaces
// the rows, but only when it differs from what the rows already say, so typing is not
// interrupted by the echo of its own commit.
watch(
  () => props.settings.authorAliasesJson,
  (value) => {
    if (value !== serialize(aliases.value)) aliases.value = parse(value)
  },
)

function serialize(rows: AliasRow[]): string {
  return JSON.stringify(
    rows
      .map((row) => ({ variant: row.variant.trim(), canonical: row.canonical.trim() }))
      .filter((row) => row.variant && row.canonical),
  )
}

function commit() {
  emit('update:settings', { ...props.settings, authorAliasesJson: serialize(aliases.value) })
}

function add() {
  aliases.value.push({ variant: '', canonical: '' })
}

function remove(index: number) {
  aliases.value.splice(index, 1)
  commit()
}

async function applyToLibrary() {
  applying.value = true
  applyMessage.value = null
  try {
    const result = await apiService.applyAuthorAliases()
    applyMessage.value = `Renamed ${result.booksChanged} of ${result.booksScanned} books.`
  } catch (err) {
    logger.error('Failed to apply author aliases', err)
    applyMessage.value = err instanceof Error ? err.message : String(err)
  } finally {
    applying.value = false
  }
}
</script>

<style scoped>
.alias-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.alias-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.alias-row input {
  flex: 1;
  min-width: 0;
  padding: 0.6rem 0.85rem;
  border: 1px solid #444;
  border-radius: 6px;
  background-color: #1a1a1a;
  color: #fff;
  font-size: 0.95rem;
}

.alias-row input::placeholder {
  color: #6c757d;
}

.alias-row input:focus {
  outline: none;
  border-color: var(--brand-500);
  box-shadow: 0 0 0 3px rgba(77, 171, 247, 0.1);
}

.alias-arrow {
  color: var(--text-secondary, #adb5bd);
}

.alias-remove {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 1.75rem;
  height: 1.75rem;
  border: 1px solid var(--border-color, #343a40);
  border-radius: 4px;
  background: transparent;
  color: var(--text-secondary, #adb5bd);
  cursor: pointer;
}

.alias-remove:hover {
  color: #ff6b6b;
  border-color: #ff6b6b;
}

.alias-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
  margin-top: 0.25rem;
}

.alias-note {
  font-size: 0.8125rem;
  color: var(--text-secondary, #adb5bd);
}
</style>

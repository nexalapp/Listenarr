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
    <h3><PhMagnifyingGlass /> Search Settings</h3>
    <div class="form-body">
      <div class="form-row">
        <div class="form-group">
          <label for="default-search-region">Preferred Default Region</label>
          <select
            id="default-search-region"
            :value="defaultSearchRegion"
            class="form-select"
            @change="updateDefaultSearchRegion"
          >
            <option v-for="option in searchRegionOptions" :key="option.value" :value="option.value">
              {{ option.label }}
            </option>
          </select>
          <small class="form-help">
            Used as the default market in Add New searches. You can still change it per search.
          </small>
        </div>

        <div class="form-group">
          <label for="library-languages">Your Languages</label>
          <select
            id="library-languages"
            class="form-select library-languages"
            multiple
            :size="6"
            @change="updateLibraryLanguages"
          >
            <option
              v-for="option in libraryLanguageOptions"
              :key="option.value"
              :value="option.value"
              :selected="libraryLanguages.includes(option.value)"
            >
              {{ option.label }}
            </option>
          </select>
          <small class="form-help">
            The languages you read in. Searches, author and series pages and Suggested stay within
            them; a translation into any other language is left out. Select none for all languages.
          </small>
        </div>
      </div>

      <div class="form-row">
        <div class="form-group">
          <label for="suggestions-background-fetch">Suggested Catalog Fetch Interval</label>
          <input
            id="suggestions-background-fetch"
            :value="settings.suggestionsBackgroundFetchIntervalMinutes ?? 1"
            type="number"
            min="0"
            max="1440"
            class="form-input"
            @input="updateSuggestionsBackgroundFetchInterval"
          />
          <small class="form-help">
            Minutes between background fetches of one author or series catalog that Suggested is
            missing. One a minute fills in a library of a few hundred over a few hours without
            getting in the way of your own searches. Zero turns it off; the page's Fetch catalogs
            button still works.
          </small>
        </div>
      </div>

      <CheckboxCard
        :modelValue="settings.enableOpenLibrarySearch"
        @update:modelValue="updateEnableOpenLibrarySearch"
        title="Enable OpenLibrary Searching"
        description="Include OpenLibrary title augmentation and lookups when performing intelligent searches."
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { ApplicationSettings } from '@/types'
import { PhMagnifyingGlass } from '@phosphor-icons/vue'
// Checkbox usage handled via CheckboxCard; no direct Checkbox import needed here
import CheckboxCard from '@/components/settings/CheckboxCard.vue'
import {
  normalizePreferredSearchLanguage,
  normalizeSearchRegion,
  preferredSearchLanguageOptions,
  searchRegionOptions,
} from '@/utils/languageMapping'

const props = defineProps<{ settings: Partial<ApplicationSettings> }>()
const emit = defineEmits<{
  'update:settings': [value: Partial<ApplicationSettings>]
}>()

function updateField(field: keyof ApplicationSettings, value: unknown) {
  const payload = { ...(props.settings || {}), [field]: value } as Partial<ApplicationSettings>
  emit('update:settings', payload)
}

function updateEnableOpenLibrarySearch(value: boolean) {
  updateField('enableOpenLibrarySearch', value)
}

const defaultSearchRegion = computed(() =>
  normalizeSearchRegion(props.settings.defaultSearchRegion),
)

// "All" is not a language someone reads in; the default-language field covers that.
const libraryLanguageOptions = preferredSearchLanguageOptions.filter(
  (option) => option.value !== 'all',
)

// One picker. Older settings only had the single default language; a saved
// list wins, otherwise that default is shown as the one selection.
const libraryLanguages = computed<string[]>(() => {
  try {
    const parsed = JSON.parse(props.settings.libraryLanguagesJson || '[]')
    if (Array.isArray(parsed) && parsed.length > 0) {
      return parsed.filter((v): v is string => typeof v === 'string')
    }
  } catch {
    // fall through
  }
  const single = normalizePreferredSearchLanguage(props.settings.defaultSearchLanguage)
  return single === 'all' ? [] : [single]
})

function updateLibraryLanguages(event: Event) {
  const selected = Array.from((event.target as HTMLSelectElement).selectedOptions).map(
    (option) => option.value,
  )
  // Keep the single field in step for anything that still reads it.
  emit('update:settings', {
    ...(props.settings || {}),
    libraryLanguagesJson: JSON.stringify(selected),
    defaultSearchLanguage: selected[0] ?? 'all',
  } as Partial<ApplicationSettings>)
}

function updateSuggestionsBackgroundFetchInterval(event: Event) {
  updateField(
    'suggestionsBackgroundFetchIntervalMinutes',
    Math.max(0, Number((event.target as HTMLInputElement).value || 0)),
  )
}

function updateDefaultSearchRegion(event: Event) {
  updateField('defaultSearchRegion', (event.target as HTMLSelectElement).value)
}
</script>

<style scoped>
h3 {
  margin: 0 0 1.5rem 0;
  padding: 0;
  font-size: 1.1rem;
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: #fff;
}

/* Modal-like search settings */
.form-body {
  padding: 1.25rem;
  border-radius: 6px;
  border: 1px solid #333;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.6);
  background-color: #232323;
}

.form-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
  gap: 1.5rem;
}

.form-group label {
  margin-bottom: 0.5rem;
  font-weight: 500;
  color: #fff;
}

.form-group input[type='number'] {
  width: 100%;
  padding: 0.9rem 0.85rem;
  border: 1px solid #444;
  border-radius: 6px;
  background-color: #1a1a1a;
  color: #fff;
  font-size: 0.95rem;
}

.form-group select,
.form-group input[type='number'] {
  width: 100%;
  /* The global .form-select fixes a height sized for its own padding; with this
     section's larger padding that clipped the text. Let the padding set the height. */
  height: auto;
  line-height: 1.3;
  padding: 0.9rem 0.85rem;
  border: 1px solid #444;
  border-radius: 6px;
  background-color: #1a1a1a;
  color: #fff;
  font-size: 0.95rem;
}

.form-group input:focus,
.form-group select:focus {
  outline: none;
  border-color: var(--brand-500);
  box-shadow: 0 0 0 3px rgba(77, 171, 247, 0.08);
}
.form-group input::placeholder {
  color: #6c757d;
}

.form-help {
  display: block;
  margin-top: 0.5rem;
  font-size: 0.85rem;
  color: #adb5bd;
  line-height: 1.5;
}
</style>

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
    <h3><PhTag /> Metadata Tags</h3>
    <div class="form-body">
      <FormRow
        label="Write Tags Automatically"
        help="Write the library's metadata into a book's M4B files once it lands, from a download import or a conversion. A book whose tags already match is left alone, so this costs one read per import."
      >
        <label class="tags-toggle">
          <input
            type="checkbox"
            :checked="settings.writeMetadataTags ?? false"
            @change="
              (e) => updateField('writeMetadataTags', (e.target as HTMLInputElement).checked)
            "
          />
          <span>Write tags after import</span>
        </label>
        <p class="tags-note">
          Each file is copied, tagged and read back before it replaces the original. Only MP4
          containers can be tagged — an MP3 book has to be converted first, because ID3 cannot carry
          the description atom Plex reads.
        </p>
      </FormRow>

      <FormRow
        label="Embed Cover Art"
        help="Embed the book's cached cover into a file that carries none. Art the file already has is never replaced."
      >
        <label class="tags-toggle">
          <input
            type="checkbox"
            :checked="settings.embedCoverArtInTags ?? true"
            @change="
              (e) => updateField('embedCoverArtInTags', (e.target as HTMLInputElement).checked)
            "
          />
          <span>Fill in missing cover art</span>
        </label>
      </FormRow>

      <FormRow
        label="Tag Mapping"
        help="What goes into each tag, written with the same pattern language as the naming patterns. An empty token takes its brackets and separators with it, so one pattern serves a series book and a standalone alike."
      >
        <div v-if="loading" class="tags-state">Loading the tag list…</div>
        <div v-else-if="error" class="tags-state tags-error">{{ error }}</div>

        <div v-else class="tag-mapping-list">
          <div v-for="definition in definitions" :key="definition.tag" class="tag-mapping">
            <div class="tag-mapping-header">
              <span class="tag-mapping-label">{{ definition.label }}</span>
              <code class="tag-mapping-key">{{ definition.tag }}</code>
              <select
                class="tag-mapping-mode"
                :value="modeOf(definition)"
                @change="
                  (e) => setMode(definition, (e.target as HTMLSelectElement).value as TagWriteMode)
                "
              >
                <option value="Always">Always write</option>
                <option value="WhenEmpty">Only when empty</option>
                <option value="Never">Never write</option>
              </select>
            </div>

            <p class="tag-mapping-description">{{ definition.description }}</p>

            <input
              type="text"
              class="tag-mapping-pattern"
              :value="patternOf(definition)"
              :placeholder="definition.defaultPattern || 'No default'"
              :disabled="modeOf(definition) === 'Never'"
              @change="(e) => setPattern(definition, (e.target as HTMLInputElement).value)"
            />
          </div>
        </div>

        <details class="tag-token-help">
          <summary>Available tokens</summary>
          <ul>
            <li v-for="token in tokens" :key="token.name">
              <code>{{ '{' + token.name + '}' }}</code>
              <span>{{ token.description }}</span>
            </li>
          </ul>
        </details>
      </FormRow>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { PhTag } from '@phosphor-icons/vue'
import FormRow from '@/components/settings/FormRow.vue'
import { apiService } from '@/services/api'
import { logger } from '@/utils/logger'
import type { ApplicationSettings, TagDefinition, TagMapping, TagWriteMode } from '@/types'

const props = defineProps<{ settings: Partial<ApplicationSettings> }>()
const emit = defineEmits<{
  'update:settings': [value: Partial<ApplicationSettings>]
}>()

const definitions = ref<TagDefinition[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

/**
 * The tokens a pattern may use. Kept beside the mapping rather than in a separate help
 * page: the whole reason this reuses the naming pattern language is that an operator
 * should not have to look anything up twice.
 */
const tokens = [
  { name: 'Title', description: "The book's title" },
  { name: 'Subtitle', description: "The book's subtitle" },
  { name: 'Author', description: 'Author name' },
  { name: 'Narrator', description: 'Narrator name' },
  { name: 'Series', description: 'Primary series name' },
  { name: 'SeriesNumber', description: 'Position in the primary series, exactly as given' },
  {
    name: 'SeriesBrackets',
    description:
      'Every series the book is in, each bracketed — [Enderverse 07.5][Ender’s Saga 1.1]. Empty for a standalone.',
  },
  { name: 'Description', description: 'The blurb, paragraphs and all' },
  { name: 'Genre', description: "The book's first genre" },
  { name: 'Year', description: 'Publication year' },
  { name: 'Publisher', description: 'Publisher name' },
  { name: 'Language', description: 'Metadata language' },
  { name: 'Edition', description: 'Edition label' },
  { name: 'Asin', description: 'Audible identifier' },
]

const updateField = <K extends keyof ApplicationSettings>(
  key: K,
  value: ApplicationSettings[K],
) => {
  emit('update:settings', { ...props.settings, [key]: value })
}

/**
 * The saved mapping, or the catalog's defaults where the operator has changed nothing.
 * Settings written before this feature existed hold no mapping at all, and that has to
 * read as "the shipped defaults" rather than as "write no tags".
 */
const currentMappings = (): TagMapping[] =>
  props.settings.tagMappings?.length
    ? props.settings.tagMappings
    : definitions.value.map((definition) => ({
        tag: definition.tag,
        pattern: definition.pattern,
        mode: definition.mode,
      }))

const findMapping = (definition: TagDefinition): TagMapping | undefined =>
  props.settings.tagMappings?.find((mapping) => mapping.tag === definition.tag)

const patternOf = (definition: TagDefinition) =>
  findMapping(definition)?.pattern ?? definition.pattern

const modeOf = (definition: TagDefinition): TagWriteMode =>
  findMapping(definition)?.mode ?? definition.mode

function writeMapping(definition: TagDefinition, changes: Partial<TagMapping>) {
  const next = currentMappings().map((mapping) =>
    mapping.tag === definition.tag ? { ...mapping, ...changes } : mapping,
  )

  updateField('tagMappings', next)
}

const setPattern = (definition: TagDefinition, pattern: string) =>
  writeMapping(definition, { pattern })

const setMode = (definition: TagDefinition, mode: TagWriteMode) =>
  writeMapping(definition, { mode })

onMounted(async () => {
  try {
    definitions.value = await apiService.getTagDefinitions()
  } catch (err) {
    logger.warn('Failed to load tag definitions', err)
    error.value = 'The tag list could not be loaded.'
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
.tags-toggle {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
}

.tags-toggle input {
  width: 1rem;
  height: 1rem;
}

.tags-note {
  margin: 0.5rem 0 0;
  font-size: 0.8125rem;
  color: var(--text-secondary, #adb5bd);
  line-height: 1.5;
}

.tags-state {
  font-size: 0.875rem;
  color: var(--text-secondary, #adb5bd);
}

.tags-error {
  color: #ff6b6b;
}

.tag-mapping-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.tag-mapping {
  border: 1px solid var(--border-color, #343a40);
  border-radius: 6px;
  padding: 0.75rem;
  background-color: var(--bg-secondary, #212529);
}

.tag-mapping-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.tag-mapping-label {
  font-weight: 600;
  font-size: 0.9375rem;
}

.tag-mapping-key {
  font-size: 0.75rem;
  padding: 0.1rem 0.35rem;
  border-radius: 4px;
  background-color: var(--bg-tertiary, #2b3035);
  color: var(--text-secondary, #adb5bd);
}

.tag-mapping-mode {
  margin-left: auto;
  font-size: 0.8125rem;
  padding: 0.25rem 0.5rem;
}

.tag-mapping-description {
  margin: 0.4rem 0 0.55rem;
  font-size: 0.8125rem;
  color: var(--text-secondary, #adb5bd);
  line-height: 1.45;
}

.tag-mapping-pattern {
  width: 100%;
  font-family: var(--font-mono, monospace);
  font-size: 0.8125rem;
}

.tag-mapping-pattern:disabled {
  opacity: 0.5;
}

.tag-token-help {
  margin-top: 0.75rem;
  font-size: 0.8125rem;
  color: var(--text-secondary, #adb5bd);
}

.tag-token-help summary {
  cursor: pointer;
}

.tag-token-help ul {
  list-style: none;
  margin: 0.5rem 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.tag-token-help li {
  display: flex;
  gap: 0.5rem;
  align-items: baseline;
}

.tag-token-help code {
  flex-shrink: 0;
  min-width: 9rem;
}

@media (max-width: 640px) {
  .tag-mapping-mode {
    margin-left: 0;
    width: 100%;
  }

  .tag-token-help li {
    flex-direction: column;
    gap: 0.1rem;
  }
}
</style>

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
        label="Transcription"
        help="Lets a chapter repair listen to the audio. A CD rip's tracks outnumber its chapters; hearing the narrator announce “Chapter Four” at a track is how the author's chapters are found among them, and how placeholder titles get their names. Runs on the CPU and downloads a 150 MB model the first time it is needed. Found books listen to their own credits whatever this says: identifying a book whose tags say nothing is what the Found tab is for."
      >
        <label class="tags-toggle">
          <input
            type="checkbox"
            :checked="settings.transcriptionEnabled ?? false"
            @change="
              (e) => updateField('transcriptionEnabled', (e.target as HTMLInputElement).checked)
            "
          />
          <span>Listen to audio when repairing chapters</span>
        </label>
        <select
          class="tag-mapping-mode transcription-model"
          :value="settings.transcriptionModel ?? 'base.en'"
          :disabled="!(settings.transcriptionEnabled ?? false)"
          aria-label="Whisper model"
          @change="(e) => updateField('transcriptionModel', (e.target as HTMLSelectElement).value)"
        >
          <option value="tiny.en">tiny.en — fastest, least accurate</option>
          <option value="base.en">base.en — hears chapter announcements well</option>
          <option value="small.en">small.en — better with names, about 3× slower</option>
        </select>
        <div
          v-if="settings.transcriptionEnabled"
          class="transcription-model-status"
          :class="`transcription-model-status--${modelStatus?.state ?? 'unknown'}`"
        >
          <PhSpinner v-if="modelStatus?.state === 'downloading'" class="ph-spin" :size="14" />
          <PhCheckCircle v-else-if="modelStatus?.state === 'ready'" :size="14" />
          <PhWarningCircle v-else-if="modelStatus?.state === 'failed'" :size="14" />
          <PhDownloadSimple v-else :size="14" />
          <span>{{ modelStatusText }}</span>
          <button
            v-if="
              modelStatus && modelStatus.state !== 'ready' && modelStatus.state !== 'downloading'
            "
            type="button"
            class="transcription-download-btn"
            :disabled="downloading"
            @click="downloadModel"
          >
            {{ modelStatus.state === 'failed' ? 'Try again' : 'Download now' }}
          </button>
        </div>
        <label class="tags-toggle transcription-audit">
          <input
            type="checkbox"
            :checked="settings.audioAuditOnImport ?? false"
            :disabled="!(settings.transcriptionEnabled ?? false)"
            @change="
              (e) => updateField('audioAuditOnImport', (e.target as HTMLInputElement).checked)
            "
          />
          <span
            >Transcribe every new import: hear its opening and closing and check them against the
            record</span
          >
        </label>
      </FormRow>

      <FormRow
        label="Audio quality probe"
        help="Decode a couple of minutes of each book and judge what the recording sounds like — hiss, muffle, codec damage. Seconds of CPU per book, nothing written to any file. Feeds the audio part of the health score; while off, it stays unknown."
      >
        <label class="tags-toggle">
          <input
            type="checkbox"
            :checked="settings.audioQualityProbeEnabled ?? true"
            @change="
              (e) => updateField('audioQualityProbeEnabled', (e.target as HTMLInputElement).checked)
            "
          />
          <span>Probe new imports, and sweep the existing library once</span>
        </label>
      </FormRow>

      <FormRow
        label="Audio repair"
        help="Repair a damaged recording by running it through a generative speech-enhancement model. The first repair downloads the model into the config volume (about 4 GB, once); a repair holds 5–10 GB of memory while it runs and takes one to two times the book's length."
      >
        <label class="tags-toggle">
          <input
            type="checkbox"
            :checked="settings.repairEnabled ?? true"
            @change="(e) => toggleRepair((e.target as HTMLInputElement).checked)"
          />
          <span>Offer audio repair, and provision the model on first enable</span>
        </label>
        <div v-if="enhancerStatus" class="enhancer-status">
          <template v-if="enhancerStatus.state === 'Provisioning'">
            Downloading the model: {{ enhancerStatus.provisionStep }} ({{
              Math.round(enhancerStatus.provisionProgress)
            }}%)
          </template>
          <template v-else-if="enhancerStatus.state === 'Ready'">
            Model ready{{
              enhancerStatus.diskBytes
                ? ` · ${(enhancerStatus.diskBytes / 1_000_000_000).toFixed(1)} GB on disk`
                : ''
            }}
          </template>
          <template v-else-if="enhancerStatus.state === 'Failed'">
            Provisioning failed: {{ enhancerStatus.error }}
            <button type="button" class="btn btn-sm btn-secondary" @click="provision">
              Try again
            </button>
          </template>
        </div>
      </FormRow>

      <FormRow
        label="Auto-repair on import"
        help="Fix a newly imported book as soon as its checks find a need, without asking. Only imports: books already in the library are repaired from the library's Actions menu."
      >
        <label class="tags-toggle">
          <input
            type="checkbox"
            :checked="settings.autoRepairChapters ?? true"
            @change="
              (e) => updateField('autoRepairChapters', (e.target as HTMLInputElement).checked)
            "
          />
          <span>Auto-repair chapters: apply the chapter fix once it is worked out</span>
        </label>
        <label class="tags-toggle">
          <input
            type="checkbox"
            :checked="settings.autoRepairAudio ?? true"
            :disabled="settings.repairEnabled === false"
            @change="(e) => updateField('autoRepairAudio', (e.target as HTMLInputElement).checked)"
          />
          <span>Auto-repair audio: queue a repair when the probe recommends one</span>
        </label>
      </FormRow>

      <FormRow
        v-if="settings.repairEnabled"
        label="Repaired originals"
        help="A repair is a lossy, opinionated rewrite; the original is the only way back. Archive uses the same location as conversion."
      >
        <select
          class="form-select"
          :value="settings.repairSourceDisposition ?? 'Archive'"
          @change="
            (e) =>
              updateField(
                'repairSourceDisposition',
                (e.target as HTMLSelectElement).value as 'Archive' | 'Keep' | 'Delete',
              )
          "
        >
          <option value="Archive">Archive them</option>
          <option value="Keep">Leave them on disk</option>
          <option value="Delete">Delete them</option>
        </select>
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
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  PhCheckCircle,
  PhDownloadSimple,
  PhSpinner,
  PhTag,
  PhWarningCircle,
} from '@phosphor-icons/vue'
import FormRow from '@/components/settings/FormRow.vue'
import { apiService } from '@/services/api'
import { logger } from '@/utils/logger'
import type {
  ApplicationSettings,
  EnhancerStatusView,
  TagDefinition,
  TagMapping,
  TagWriteMode,
  TranscriptionModelStatus,
} from '@/types'

const props = defineProps<{ settings: Partial<ApplicationSettings> }>()
const emit = defineEmits<{
  'update:settings': [value: Partial<ApplicationSettings>]
}>()

const enhancerStatus = ref<EnhancerStatusView | null>(null)
let enhancerPoll: ReturnType<typeof setTimeout> | null = null

function toggleRepair(enabled: boolean) {
  updateField('repairEnabled', enabled)
  if (enabled) {
    void provision()
  }
}

async function provision() {
  try {
    enhancerStatus.value = await apiService.provisionEnhancer()
    pollEnhancer()
  } catch (err) {
    logger.error('Could not start provisioning the enhancer', err)
  }
}

function pollEnhancer() {
  if (enhancerPoll != null) clearTimeout(enhancerPoll)
  enhancerPoll = setTimeout(async () => {
    try {
      enhancerStatus.value = await apiService.getEnhancerStatus()
      if (enhancerStatus.value?.state === 'Provisioning') pollEnhancer()
    } catch (err) {
      logger.error('Could not read the enhancer status', err)
    }
  }, 4000)
}

onMounted(async () => {
  // Show where the model stands if repair is already on; a fresh page should not
  // claim NotProvisioned while a download is half way.
  if (props.settings.repairEnabled) {
    try {
      enhancerStatus.value = await apiService.getEnhancerStatus()
      if (enhancerStatus.value?.state === 'Provisioning') pollEnhancer()
    } catch (err) {
      logger.error('Could not read the enhancer status', err)
    }
  }
})

onBeforeUnmount(() => {
  if (enhancerPoll != null) clearTimeout(enhancerPoll)
})

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
  { name: 'Author', description: 'The primary (first) author alone' },
  { name: 'Authors', description: 'Every credited author, comma-separated' },
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
  // Turning transcription on, or choosing a model, is the moment to fetch it: the
  // download then runs while the operator is still on this page, not inside the
  // first repair that needs it.
  if ((key === 'transcriptionEnabled' && value === true) || key === 'transcriptionModel') {
    void downloadModel()
  }
}

// ---- whisper model ---------------------------------------------------------------

const modelStatus = ref<TranscriptionModelStatus | null>(null)
const downloading = ref(false)
let poll: ReturnType<typeof setTimeout> | null = null

const selectedModel = computed(() => props.settings.transcriptionModel ?? 'base.en')

const modelStatusText = computed(() => {
  const status = modelStatus.value
  if (!status) return `Checking for the ${selectedModel.value} model…`
  const mb = status.sizeBytes != null ? `${Math.round(status.sizeBytes / 1_048_576)} MB` : null
  switch (status.state) {
    case 'ready':
      return `Model ${status.model} is on disk${mb ? ` (${mb})` : ''}.`
    case 'downloading':
      return `Downloading ${status.model}${mb ? `… ${mb} so far` : '…'}`
    case 'failed':
      return `Model ${status.model} could not be downloaded: ${status.error ?? 'unknown error'}`
    default:
      return `Model ${status.model} is not downloaded yet; the first repair that listens would wait for it.`
  }
})

function stopPolling() {
  if (poll) {
    clearTimeout(poll)
    poll = null
  }
}

async function refreshModelStatus() {
  stopPolling()
  try {
    modelStatus.value = await apiService.getTranscriptionModel(selectedModel.value)
  } catch (err) {
    logger.warn('Failed to read the whisper model status', err)
    return
  }
  if (modelStatus.value?.state === 'downloading') {
    poll = setTimeout(() => void refreshModelStatus(), 3000)
  }
}

async function downloadModel() {
  downloading.value = true
  try {
    modelStatus.value = await apiService.downloadTranscriptionModel(selectedModel.value)
    stopPolling()
    if (modelStatus.value.state === 'downloading') {
      poll = setTimeout(() => void refreshModelStatus(), 3000)
    }
  } catch (err) {
    logger.warn('Failed to start the whisper model download', err)
  } finally {
    downloading.value = false
  }
}

watch(
  () => [props.settings.transcriptionEnabled, selectedModel.value] as const,
  ([enabled]) => {
    if (enabled) void refreshModelStatus()
    else stopPolling()
  },
  { immediate: true },
)
onBeforeUnmount(stopPolling)

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
.enhancer-status {
  margin-top: 0.5rem;
  color: var(--text-secondary);
  font-size: 0.88rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

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

.transcription-model-status {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 8px;
  font-size: 12px;
  color: var(--text-secondary, #aaa);
}

.transcription-model-status--ready {
  color: var(--success-500, #4caf50);
}

.transcription-model-status--failed {
  color: var(--danger-500, #ff6b6b);
}

.transcription-download-btn {
  padding: 2px 10px;
  border: 1px solid var(--brand-500);
  border-radius: 6px;
  background: transparent;
  color: var(--brand-500);
  font-size: 12px;
  cursor: pointer;
}

.transcription-model {
  margin-top: 0.5rem;
  display: block;
}

.transcription-audit {
  margin-top: 0.5rem;
}
</style>

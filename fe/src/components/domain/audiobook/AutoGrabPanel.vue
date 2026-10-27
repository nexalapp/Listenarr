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
  <div class="autograb">
    <button
      ref="trigger"
      type="button"
      class="btn btn-secondary autograb-trigger"
      :class="{ on: store.enabled }"
      :aria-expanded="open"
      aria-haspopup="true"
      title="Add books from the lists you follow, without being asked each time"
      @click="toggleOpen"
    >
      <PhDownloadSimple :size="14" />
      Auto-grab
      <span v-if="store.enabled" class="autograb-state">on</span>
    </button>

    <div v-if="open" class="autograb-panel" role="dialog" aria-label="Auto-grab">
      <LoadingState v-if="store.loading && !store.settings" message="Reading the settings…" />

      <template v-else>
        <label class="autograb-switch">
          <input type="checkbox" :checked="store.enabled" @change="setEnabled" />
          <span>
            <strong>Grab from these lists on a schedule</strong>
            <small>Off, nothing is added until you press Run now.</small>
          </span>
        </label>

        <p v-if="thanks" class="autograb-thanks">
          <strong>{{ thanks.thisHour }}</strong> of {{ thanks.perHour }} abook.link thanks this
          hour, {{ thanks.today }} today.
          <span v-if="!thanks.allowed" class="autograb-thanks-blocked">{{ thanks.reason }}</span>
          <span v-else>A grab from abook.link has to press thank, so this is the ceiling on it.</span>
        </p>

        <div class="autograb-numbers">
          <label>
            <span>Books per run</span>
            <input :value="perRun" type="number" min="1" :max="200" @change="setPerRun" />
          </label>
          <label>
            <span>Every</span>
            <input
              :value="intervalMinutes"
              type="number"
              min="5"
              :max="1440"
              @change="setInterval"
            />
            <small>minutes</small>
          </label>
        </div>

        <p class="autograb-hint">
          The order decides whose books a run's allowance is spent on. Drag is overkill for five
          rows, so use the arrows.
        </p>

        <div class="autograb-prepare">
          <label>
            <input type="checkbox" :checked="prepareEnabled" @change="setPrepare" />
            <span>
              Get downloads ready in the background
              <small>
                Thanks the post, reads its payload and finds the NZB when the book is found, so
                adding it is a send and nothing more.
              </small>
            </span>
          </label>
          <label :class="{ disabled: !prepareEnabled }">
            <input
              type="checkbox"
              :checked="prepareMetered"
              :disabled="!prepareEnabled"
              @change="setPrepareMetered"
            />
            <span>
              …and may spend NZBKing tokens doing it
              <small>
                Off, a release the free indexes do not hold is marked as needing NZBKing and left
                for a grab you ask for, which may still spend one. Thanking and the free indexes are
                unaffected either way.
              </small>
            </span>
          </label>
        </div>

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

        <div class="autograb-preview">
          <button type="button" class="link-btn" :disabled="store.loading" @click="store.preview()">
            What would it grab?
          </button>
          <template v-if="store.plan">
            <p v-if="store.plan.unavailable" class="autograb-note">{{ store.plan.unavailable }}</p>
            <p v-else class="autograb-note">
              Would grab {{ store.plan.candidates.length }} of {{ store.plan.available }} waiting:
              <template v-for="(count, index) in withBooks" :key="count.label">
                {{ index > 0 ? ', ' : '' }}{{ count.available }} from {{ count.label }}
              </template>
            </p>
            <ol v-if="store.plan.candidates.length > 0" class="autograb-books">
              <li v-for="candidate in store.plan.candidates.slice(0, 8)" :key="candidate.asin">
                {{ candidate.title }}
                <span class="autograb-dim"
                  >{{ candidate.author }} · {{ candidate.indexerName }}</span
                >
              </li>
            </ol>
          </template>
        </div>

        <div class="autograb-actions">
          <span v-if="ran" class="autograb-note">{{ ran }}</span>
          <button type="button" class="link-btn" @click="close">Close</button>
          <button
            type="button"
            class="btn btn-primary btn-sm"
            :disabled="running || rows.length === 0"
            @click="runNow"
          >
            {{ running ? 'Running…' : 'Run now' }}
          </button>
        </div>
      </template>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { PhDownloadSimple } from '@phosphor-icons/vue'
import { LoadingState } from '@/components/base'
import { apiService } from '@/services/api'
import { useToast } from '@/services/toastService'
import { describeApiError } from '@/utils/apiError'
import { useAutoGrabStore } from '@/stores/autoGrab'
import type {
  AbookThankBudget,
  ApplicationSettings,
  AutoGrabKind,
  AutoGrabSettings,
} from '@/types'

const props = defineProps<{
  /**
   * How to name a followed list, keyed "kind" or "kind:sectionId". Only the two tabs
   * that are one list need naming here; a chart category or board names itself in the
   * preview, which is where its name is known.
   */
  labels?: Record<string, string>
}>()

const store = useAutoGrabStore()
const toast = useToast()

const open = ref(false)
const running = ref(false)
const ran = ref<string | null>(null)
const trigger = ref<HTMLButtonElement | null>(null)

const appSettings = ref<ApplicationSettings | null>(null)
const thanks = ref<AbookThankBudget | null>(null)
const prepareEnabled = computed(() => appSettings.value?.prepareReleasesEnabled ?? true)
const prepareMetered = computed(
  () => appSettings.value?.prepareReleasesMayUseMeteredIndexes ?? false,
)

const perRun = computed(() => store.settings?.perRun ?? 10)
const intervalMinutes = computed(() => store.settings?.intervalMinutes ?? 60)

const withBooks = computed(() =>
  (store.plan?.bySource ?? []).filter((count) => count.available > 0),
)

function planned(key: string) {
  return store.plan?.bySource.find(
    (count) => (count.sectionId ? `${count.kind}:${count.sectionId}` : count.kind) === key,
  )
}

/** The followed lists in their stored order, named for a person. */
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

watch(
  () => store.panelRequested,
  async (asked) => {
    if (!asked) return
    store.panelOpened()
    if (!open.value) await toggleOpen()
  },
)

async function toggleOpen() {
  open.value = !open.value
  if (!open.value) return
  await store.load()
  await loadAppSettings()
  // The preview names every followed list and says how many are waiting in each, so
  // opening the panel is also how the rows get their names.
  await Promise.all([store.preview(), loadThanks()])
}

async function loadAppSettings() {
  try {
    appSettings.value = await apiService.getApplicationSettings()
  } catch {
    // The two preparation ticks stay at their defaults; the rest of the panel works.
  }
}

async function loadThanks() {
  try {
    thanks.value = await apiService.getAbookThankBudget()
  } catch {
    // The count is worth having and worth nothing if fetching it breaks the panel.
  }
}

async function savePrepare(change: Partial<ApplicationSettings>) {
  if (!appSettings.value) return
  const next = { ...appSettings.value, ...change }
  appSettings.value = next
  try {
    appSettings.value = await apiService.saveApplicationSettings(next)
  } catch (e) {
    toast.error('Not saved', describeApiError(e, 'The setting was not changed.'))
    await loadAppSettings()
  }
}

function setPrepare(event: Event) {
  return savePrepare({ prepareReleasesEnabled: (event.target as HTMLInputElement).checked })
}

function setPrepareMetered(event: Event) {
  return savePrepare({
    prepareReleasesMayUseMeteredIndexes: (event.target as HTMLInputElement).checked,
  })
}

function close() {
  open.value = false
}

function current(): AutoGrabSettings {
  return store.settings ?? { enabled: false, perRun: 10, intervalMinutes: 60, sources: [] }
}

async function setEnabled(event: Event) {
  await store.save({ ...current(), enabled: (event.target as HTMLInputElement).checked })
}

async function setPerRun(event: Event) {
  const value = Math.max(1, Number((event.target as HTMLInputElement).value || 1))
  await store.save({ ...current(), perRun: value })
}

async function setInterval(event: Event) {
  const value = Math.max(5, Number((event.target as HTMLInputElement).value || 60))
  await store.save({ ...current(), intervalMinutes: value })
}

async function move(index: number, by: number) {
  const sources = [...current().sources]
  const target = index + by
  if (target < 0 || target >= sources.length) return
  const [moved] = sources.splice(index, 1)
  sources.splice(target, 0, moved!)
  await store.save({ ...current(), sources })
}

async function runNow() {
  running.value = true
  ran.value = null
  try {
    const report = await apiService.runAutoGrab()
    ran.value =
      report.added === 0 && report.failed === 0
        ? 'Nothing was waiting.'
        : `Added ${report.added}${report.failed > 0 ? `, ${report.failed} failed` : ''}.`
    await store.preview()
  } catch (e) {
    toast.error('Auto-grab did not run', describeApiError(e, 'Nothing was added.'))
  } finally {
    running.value = false
  }
}

function onDocumentClick(event: MouseEvent) {
  if (!open.value) return
  const root = trigger.value?.parentElement
  const target = event.target as Node | null
  if (root && target && !root.contains(target)) close()
}

watch(open, (value) => {
  if (value) document.addEventListener('click', onDocumentClick)
  else document.removeEventListener('click', onDocumentClick)
})

onBeforeUnmount(() => document.removeEventListener('click', onDocumentClick))
</script>

<style scoped>
.autograb {
  position: relative;
  display: inline-block;
}

.autograb-trigger {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
}

.autograb-trigger.on {
  border-color: #3b82f6;
  color: #cfe0ff;
}

.autograb-state {
  background: #3b82f6;
  color: #fff;
  border-radius: 10px;
  padding: 0 0.4rem;
  font-size: 0.68rem;
}

.autograb-panel {
  position: absolute;
  z-index: 40;
  top: calc(100% + 0.4rem);
  right: 0;
  width: min(430px, 92vw);
  background: #232323;
  border: 1px solid #3a3a3a;
  border-radius: 6px;
  box-shadow: 0 10px 30px rgba(0, 0, 0, 0.6);
  padding: 0.9rem;
  color: #ccc;
  font-size: 0.85rem;
}

.autograb-switch {
  display: flex;
  gap: 0.5rem;
  align-items: flex-start;
  cursor: pointer;
}

.autograb-switch span {
  display: flex;
  flex-direction: column;
}

.autograb-switch strong {
  color: #eee;
  font-weight: 600;
}

.autograb-switch small,
.autograb-hint {
  color: #888;
  font-size: 0.75rem;
}

.autograb-numbers {
  display: flex;
  gap: 1.2rem;
  margin: 0.8rem 0 0.4rem;
}

.autograb-numbers label {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.autograb-numbers input {
  width: 4.5rem;
  background: #1b1b1b;
  border: 1px solid #3a3a3a;
  border-radius: 4px;
  color: #ddd;
  padding: 0.2rem 0.4rem;
}

.autograb-hint {
  margin: 0.4rem 0 0.6rem;
}

.autograb-prepare {
  border-top: 1px solid #333;
  border-bottom: 1px solid #333;
  padding: 0.6rem 0;
  margin-bottom: 0.6rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.autograb-prepare label {
  display: flex;
  gap: 0.5rem;
  align-items: flex-start;
  cursor: pointer;
}

.autograb-prepare label.disabled {
  opacity: 0.55;
  cursor: default;
}

.autograb-prepare span {
  display: flex;
  flex-direction: column;
}

.autograb-prepare small {
  color: #888;
  font-size: 0.72rem;
}

.autograb-list {
  list-style: none;
  margin: 0;
  padding: 0;
  max-height: 12rem;
  overflow-y: auto;
}

.autograb-list li {
  display: flex;
  align-items: center;
  gap: 0.45rem;
  padding: 0.2rem 0;
}

.autograb-label {
  flex: 1 1 auto;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.autograb-count {
  color: #7aa7ff;
  font-size: 0.72rem;
}

.autograb-move {
  background: none;
  border: 1px solid #3a3a3a;
  border-radius: 4px;
  color: #bbb;
  cursor: pointer;
  line-height: 1;
  padding: 0.1rem 0.3rem;
}

.autograb-move:disabled {
  opacity: 0.3;
  cursor: default;
}

.autograb-empty {
  color: #888;
  font-size: 0.8rem;
}

.autograb-preview {
  border-top: 1px solid #333;
  margin-top: 0.7rem;
  padding-top: 0.6rem;
}

.autograb-note {
  color: #aaa;
  font-size: 0.78rem;
  margin: 0.3rem 0 0;
}

.autograb-books {
  margin: 0.4rem 0 0;
  padding-left: 1.1rem;
  color: #ccc;
  font-size: 0.78rem;
}

.autograb-dim {
  color: #777;
}

.autograb-actions {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: 0.8rem;
  margin-top: 0.8rem;
}

.link-btn {
  background: none;
  border: none;
  color: #6ea8fe;
  cursor: pointer;
  font-size: 0.82rem;
  padding: 0;
}
</style>

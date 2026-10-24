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
  <Modal :visible="visible" size="lg" @close="handleClose">
    <template #header>
      <ModalHeader title="Repair Audio" :icon="PhWaveform" @close="handleClose" />
    </template>

    <template #default>
      <ModalBody compact maxHeight="72vh" class="repair-modal-body">
        <div class="info-section">
          <PhInfo />
          <p>
            A repair runs the whole book through a generative speech-enhancement model and
            re-encodes it as one M4B. It takes roughly one to two times the book's length in CPU
            time, survives restarts, and the originals are kept per the repair setting. Listen
            first: the ninety seconds below are enhanced with exactly the settings the full repair
            will use, and your ears are the acceptance test.
          </p>
        </div>

        <div v-if="error" class="repair-state repair-error">
          <PhWarningCircle class="repair-icon" />
          <p>{{ error }}</p>
        </div>

        <div
          v-else-if="!preview || preview.state === 'Pending' || preview.state === 'Enhancing'"
          class="repair-state"
        >
          <PhSpinner class="ph-spin repair-icon" />
          <p>
            {{ preview?.detail ?? 'Starting the preview…' }}
            <span v-if="preview?.state === 'Enhancing'">This takes a minute or two.</span>
          </p>
        </div>

        <div v-else-if="preview.state === 'Ready'" class="repair-ab">
          <div class="repair-clip">
            <h4>Before</h4>
            <AudioPreviewPlayer
              :preview-id="`repair-${audiobookId}-before`"
              :src="clipUrl('before')"
              label="Original"
            />
          </div>
          <div class="repair-clip">
            <h4>After</h4>
            <AudioPreviewPlayer
              :preview-id="`repair-${audiobookId}-after`"
              :src="clipUrl('after')"
              label="Enhanced"
            />
            <p class="repair-note">
              Both clips are encoded identically, so any difference you hear is the model.
            </p>
          </div>
        </div>
      </ModalBody>
    </template>

    <template #footer>
      <ModalFooter>
        <button type="button" class="btn btn-secondary" @click="handleClose">Close</button>
        <button
          type="button"
          class="btn btn-primary"
          :disabled="preview?.state !== 'Ready' || accepting"
          :title="
            preview?.state === 'Ready'
              ? 'Queue the full repair: the whole book through the model you just heard.'
              : 'Listen to the preview first.'
          "
          @click="accept"
        >
          {{ accepting ? 'Queueing…' : 'Repair the whole book' }}
        </button>
      </ModalFooter>
    </template>
  </Modal>
</template>

<script setup lang="ts">
import { ref, watch, onBeforeUnmount } from 'vue'
import { Modal, ModalBody, ModalFooter, ModalHeader } from '@/components/feedback'
import AudioPreviewPlayer from '@/components/ui/AudioPreviewPlayer.vue'
import { PhInfo, PhSpinner, PhWarningCircle, PhWaveform } from '@phosphor-icons/vue'
import { apiService } from '@/services/api'
import { useToast } from '@/services/toastService'
import type { RepairPreviewView } from '@/types'

const props = defineProps<{ visible: boolean; audiobookId: number | null }>()
const emit = defineEmits<{ close: []; queued: [jobId?: string] }>()

const preview = ref<RepairPreviewView | null>(null)
const error = ref<string | null>(null)
const accepting = ref(false)
let pollTimer: ReturnType<typeof setTimeout> | null = null

watch(
  () => props.visible,
  (visible) => {
    if (visible && props.audiobookId != null) {
      start(props.audiobookId)
    } else {
      stopPolling()
      preview.value = null
      error.value = null
    }
  },
)

onBeforeUnmount(stopPolling)

async function start(audiobookId: number) {
  error.value = null
  preview.value = null
  try {
    preview.value = await apiService.startRepairPreview(audiobookId)
    poll()
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  }
}

function poll() {
  stopPolling()
  pollTimer = setTimeout(async () => {
    if (!props.visible || !preview.value) return
    try {
      preview.value = await apiService.getRepairPreview(preview.value.previewId)
      if (preview.value.state === 'Failed') {
        error.value = preview.value.detail ?? 'The preview failed.'
      } else if (preview.value.state !== 'Ready') {
        poll()
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : String(err)
    }
  }, 3000)
}

function stopPolling() {
  if (pollTimer != null) {
    clearTimeout(pollTimer)
    pollTimer = null
  }
}

function clipUrl(variant: 'before' | 'after'): string {
  return preview.value ? apiService.buildRepairPreviewClipUrl(preview.value.previewId, variant) : ''
}

async function accept() {
  if (props.audiobookId == null) return
  accepting.value = true
  const toast = useToast()
  try {
    const result = await apiService.repairAudio(props.audiobookId)
    if (result.queued) {
      toast.success(
        'Repair queued',
        'The whole book goes through the model now — hours to days. Watch it in Activity.',
      )
      emit('queued', result.jobId)
      emit('close')
    } else {
      toast.error('Could not queue the repair', result.reason ?? 'It was refused.')
    }
  } catch (err) {
    toast.error('Could not queue the repair', err instanceof Error ? err.message : String(err))
  } finally {
    accepting.value = false
  }
}

function handleClose() {
  emit('close')
}
</script>

<style scoped>
.repair-modal-body {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.info-section {
  display: flex;
  gap: 0.6rem;
  align-items: flex-start;
  padding: 0.75rem;
  border-radius: 8px;
  background: var(--bg-tertiary, rgba(128, 128, 128, 0.08));
  color: var(--text-secondary);
}

.info-section p {
  margin: 0;
  font-size: 0.9rem;
}

.repair-state {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1.5rem 0.5rem;
  color: var(--text-secondary);
}

.repair-error {
  color: var(--color-error, #e05d5d);
}

.repair-icon {
  font-size: 1.4rem;
  flex-shrink: 0;
}

.repair-ab {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

@media (max-width: 640px) {
  .repair-ab {
    grid-template-columns: 1fr;
  }
}

.repair-clip h4 {
  margin: 0 0 0.5rem;
}

.repair-note {
  margin: 0.5rem 0 0;
  color: var(--text-secondary);
  font-size: 0.85rem;
}
</style>

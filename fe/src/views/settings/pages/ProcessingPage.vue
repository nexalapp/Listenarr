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
  <SettingsPageShell
    title="Conversion & Tags"
    description="What happens to a book's files after they land: MP3 to M4B conversion, the metadata written into each file, and the transcription that repairs chapters."
    :icon="PhWaveform"
    show-save
    :saving="form.isSaving.value"
    @save="form.save()"
  >
    <div v-if="form.settings.value" class="settings-form">
      <FileManagementSection
        :settings="form.settings.value"
        part="conversion"
        @update:settings="onUpdate"
      />
      <MetadataTagsSection :settings="form.settings.value" @update:settings="onUpdate" />
    </div>
  </SettingsPageShell>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { PhWaveform } from '@phosphor-icons/vue'
import SettingsPageShell from '@/views/settings/SettingsPageShell.vue'
import FileManagementSection from '@/components/settings/FileManagementSection.vue'
import MetadataTagsSection from '@/components/settings/MetadataTagsSection.vue'
import { useApplicationSettingsForm } from '@/composables/useApplicationSettingsForm'
import type { ApplicationSettings } from '@/types'

const form = useApplicationSettingsForm('ProcessingPage')

function onUpdate(value: Partial<ApplicationSettings>) {
  form.update({ ...form.settings.value, ...value } as ApplicationSettings)
}

onMounted(() => form.load())
</script>

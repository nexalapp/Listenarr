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
    title="Download Clients"
    description="The clients that fetch releases, the path mappings that let Listenarr see what they wrote, and how the download pipeline paces itself."
    :icon="PhDownload"
    show-save
    :saving="form.isSaving.value"
    @save="form.save()"
  >
    <template #actions>
      <button class="btn btn-primary" @click="clientsRef?.openAddClient()">
        <PhPlus />
        Add Download Client
      </button>
      <button class="btn btn-primary" @click="clientsRef?.openAddMapping()">
        <PhPlus />
        Add Mapping
      </button>
    </template>

    <DownloadClientsTab ref="clientsRef" />

    <div v-if="form.settings.value" class="settings-form">
      <DownloadSettingsSection
        :settings="form.settings.value"
        part="downloads"
        @update:settings="onUpdate"
      />
      <FeaturesSection
        :settings="form.settings.value"
        :only="['showCompletedExternalDownloads']"
        heading="Activity"
        @update:settings="onUpdate"
      />
    </div>
  </SettingsPageShell>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { PhDownload, PhPlus } from '@phosphor-icons/vue'
import SettingsPageShell from '@/views/settings/SettingsPageShell.vue'
import DownloadClientsTab from '@/views/settings/DownloadClientsTab.vue'
import DownloadSettingsSection from '@/components/settings/DownloadSettingsSection.vue'
import FeaturesSection from '@/components/settings/FeaturesSection.vue'
import { useApplicationSettingsForm } from '@/composables/useApplicationSettingsForm'
import { useConfigurationStore } from '@/stores/configuration'
import type { ApplicationSettings } from '@/types'

const form = useApplicationSettingsForm('DownloadClientsPage')
const configStore = useConfigurationStore()
const clientsRef = ref<InstanceType<typeof DownloadClientsTab> | null>(null)

function onUpdate(value: Partial<ApplicationSettings>) {
  form.update({ ...form.settings.value, ...value } as ApplicationSettings)
}

onMounted(async () => {
  await Promise.all([configStore.loadDownloadClientConfigurations(), form.load()])
})
</script>

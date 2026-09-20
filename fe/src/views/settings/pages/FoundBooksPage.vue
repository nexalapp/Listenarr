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
    title="Found Books"
    description="Books that turn up in the download folders without being asked for: which folders are watched, how often, whether certain matches are added on their own, and how many files a scan probes at once."
    :icon="PhFolderOpen"
    show-save
    :saving="form.isSaving.value"
    @save="form.save()"
  >
    <div v-if="form.settings.value" class="settings-form">
      <FoundBooksSection :settings="form.settings.value" @update:settings="onUpdate" />
      <DownloadSettingsSection
        :settings="form.settings.value"
        part="scan"
        @update:settings="onUpdate"
      />
    </div>
  </SettingsPageShell>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { PhFolderOpen } from '@phosphor-icons/vue'
import SettingsPageShell from '@/views/settings/SettingsPageShell.vue'
import FoundBooksSection from '@/components/settings/FoundBooksSection.vue'
import DownloadSettingsSection from '@/components/settings/DownloadSettingsSection.vue'
import { useApplicationSettingsForm } from '@/composables/useApplicationSettingsForm'
import type { ApplicationSettings } from '@/types'

const form = useApplicationSettingsForm('FoundBooksPage')

function onUpdate(value: Partial<ApplicationSettings>) {
  form.update({ ...form.settings.value, ...value } as ApplicationSettings)
}

onMounted(() => form.load())
</script>

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
    title="Media Management"
    description="Where the library lives, how books and files are named, and what happens to a download's files once they are imported."
    :icon="PhFolder"
    show-save
    :saving="form.isSaving.value"
    @save="form.save()"
  >
    <template #actions>
      <button
        class="btn btn-primary"
        :disabled="!filesystemReadinessStore.filesystemReady"
        :title="
          !filesystemReadinessStore.filesystemReady
            ? 'Available after library filesystem initialization completes'
            : undefined
        "
        @click="rootFoldersRef?.openAddRootFolder()"
      >
        <PhPlus />
        Add Root Folder
      </button>
    </template>

    <RootFoldersTab ref="rootFoldersRef" />

    <div v-if="form.settings.value" class="settings-form">
      <FileManagementSection
        :settings="form.settings.value"
        part="naming"
        @update:settings="onUpdate"
      />
    </div>
  </SettingsPageShell>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { PhFolder, PhPlus } from '@phosphor-icons/vue'
import SettingsPageShell from '@/views/settings/SettingsPageShell.vue'
import RootFoldersTab from '@/views/settings/RootFoldersTab.vue'
import FileManagementSection from '@/components/settings/FileManagementSection.vue'
import { useApplicationSettingsForm } from '@/composables/useApplicationSettingsForm'
import { useFilesystemReadinessStore } from '@/stores/filesystemReadiness'
import type { ApplicationSettings } from '@/types'

const form = useApplicationSettingsForm('MediaManagementPage')
const filesystemReadinessStore = useFilesystemReadinessStore()
const rootFoldersRef = ref<InstanceType<typeof RootFoldersTab> | null>(null)

function onUpdate(value: Partial<ApplicationSettings>) {
  form.update({ ...form.settings.value, ...value } as ApplicationSettings)
}

onMounted(() => form.load())
</script>

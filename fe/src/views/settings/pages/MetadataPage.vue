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
    title="Metadata"
    description="Where book information comes from: whether it is fetched at all, cover art, the catalogue region and languages, and the name aliases that keep one author under one name."
    :icon="PhBookOpenText"
    show-save
    :saving="form.isSaving.value"
    @save="form.save()"
  >
    <div v-if="form.settings.value" class="settings-form">
      <FeaturesSection
        :settings="form.settings.value"
        :only="['enableMetadataProcessing', 'enableCoverArtDownload']"
        heading="Fetching"
        @update:settings="onUpdate"
      />
      <SearchSettingsSection :settings="form.settings.value" @update:settings="onUpdate" />
      <AuthorAliasesSection :settings="form.settings.value" @update:settings="onUpdate" />
    </div>
  </SettingsPageShell>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { PhBookOpenText } from '@phosphor-icons/vue'
import SettingsPageShell from '@/views/settings/SettingsPageShell.vue'
import FeaturesSection from '@/components/settings/FeaturesSection.vue'
import SearchSettingsSection from '@/components/settings/SearchSettingsSection.vue'
import AuthorAliasesSection from '@/components/settings/AuthorAliasesSection.vue'
import { useApplicationSettingsForm } from '@/composables/useApplicationSettingsForm'
import type { ApplicationSettings } from '@/types'

const form = useApplicationSettingsForm('MetadataPage')

function onUpdate(value: Partial<ApplicationSettings>) {
  form.update({ ...form.settings.value, ...value } as ApplicationSettings)
}

onMounted(() => form.load())
</script>

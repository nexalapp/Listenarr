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
    title="Indexers"
    description="Where releases are searched for."
    :icon="PhListMagnifyingGlass"
  >
    <template #actions>
      <button class="btn btn-primary" @click="indexersRef?.openAddIndexer()">
        <PhPlus />
        Add Indexer
      </button>
      <button class="btn btn-primary" @click="indexersRef?.openProwlarrImport()">
        <PhDownloadSimple />
        Import from Prowlarr
      </button>
    </template>
    <IndexersTab ref="indexersRef" />
  </SettingsPageShell>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { PhDownloadSimple, PhListMagnifyingGlass, PhPlus } from '@phosphor-icons/vue'
import SettingsPageShell from '@/views/settings/SettingsPageShell.vue'
import IndexersTab from '@/views/settings/IndexersTab.vue'

const indexersRef = ref<InstanceType<typeof IndexersTab> | null>(null)
</script>

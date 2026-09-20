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
    title="Quality Profiles"
    description="Which formats and bitrates a book is allowed to arrive in, and when a better one is worth replacing it."
    :icon="PhStar"
  >
    <template #actions>
      <button class="btn btn-primary" @click="profilesRef?.openAddProfile()">
        <PhPlus />
        Add Quality Profile
      </button>
    </template>
    <QualityProfilesTab ref="profilesRef" />
  </SettingsPageShell>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { PhPlus, PhStar } from '@phosphor-icons/vue'
import SettingsPageShell from '@/views/settings/SettingsPageShell.vue'
import QualityProfilesTab from '@/views/settings/QualityProfilesTab.vue'

const profilesRef = ref<InstanceType<typeof QualityProfilesTab> | null>(null)
</script>

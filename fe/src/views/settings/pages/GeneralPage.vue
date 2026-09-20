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
    title="General"
    description="What has no better home."
    :icon="PhSliders"
    show-save
    :saving="form.isSaving.value"
    @save="form.save()"
  >
    <div v-if="form.settings.value" class="settings-form">
      <div class="form-section">
        <h3><PhClockCounterClockwise /> History</h3>
        <div class="form-body">
          <FormRow
            label="History Retention (days)"
            help="How long activity history is kept. Zero keeps it forever."
          >
            <input
              :value="form.settings.value.historyRetentionDays ?? 0"
              type="number"
              min="0"
              @input="
                (e) =>
                  onUpdate({
                    historyRetentionDays: Math.max(
                      0,
                      Number((e.target as HTMLInputElement).value || 0),
                    ),
                  })
              "
            />
          </FormRow>
        </div>
      </div>
    </div>
  </SettingsPageShell>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { PhClockCounterClockwise, PhSliders } from '@phosphor-icons/vue'
import SettingsPageShell from '@/views/settings/SettingsPageShell.vue'
import FormRow from '@/components/settings/FormRow.vue'
import { useApplicationSettingsForm } from '@/composables/useApplicationSettingsForm'
import type { ApplicationSettings } from '@/types'

const form = useApplicationSettingsForm('GeneralPage')

function onUpdate(value: Partial<ApplicationSettings>) {
  form.update({ ...form.settings.value, ...value } as ApplicationSettings)
}

onMounted(() => form.load())
</script>

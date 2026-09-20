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
    title="Connect"
    description="The things that get told when something happens: webhooks, which events fire them, and the Discord bot."
    :icon="PhBell"
    show-save
    :saving="form.isSaving.value"
    @save="form.save()"
  >
    <template #actions>
      <button class="btn btn-primary" @click="notificationsRef?.openWebhookForm()">
        <PhPlus />
        Add Webhook
      </button>
      <button
        class="btn btn-primary"
        :disabled="testingDiscord || !canTestDiscord"
        :title="
          canTestDiscord
            ? 'Test Discord integration'
            : `Bot status: ${discordBotStatus}. Fill Application ID and Bot Token, and start the bot to enable`
        "
        @click="testDiscordIntegration"
      >
        <PhSpinner v-if="testingDiscord" class="ph-spin" />
        <PhCheck v-else />
        Test Discord
      </button>
    </template>

    <div v-if="form.settings.value" class="settings-form">
      <FeaturesSection
        :settings="form.settings.value"
        :only="['enableNotifications']"
        heading="Notifications"
        @update:settings="onUpdate"
      />
    </div>

    <NotificationsTab
      v-if="form.settings.value"
      ref="notificationsRef"
      :settings="form.settings.value"
      @update:settings="form.update"
    />

    <DiscordBotTab
      v-if="form.settings.value"
      :settings="form.settings.value"
      @bot-action-completed="checkDiscordBotRunning"
    />
  </SettingsPageShell>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { PhBell, PhCheck, PhPlus, PhSpinner } from '@phosphor-icons/vue'
import SettingsPageShell from '@/views/settings/SettingsPageShell.vue'
import NotificationsTab from '@/views/settings/NotificationsTab.vue'
import DiscordBotTab from '@/views/settings/DiscordBotTab.vue'
import FeaturesSection from '@/components/settings/FeaturesSection.vue'
import { useApplicationSettingsForm } from '@/composables/useApplicationSettingsForm'
import { apiService } from '@/services/api'
import { errorTracking } from '@/services/errorTracking'
import { useToast } from '@/services/toastService'
import { describeApiError } from '@/utils/apiError'
import type { ApplicationSettings } from '@/types'

const form = useApplicationSettingsForm('ConnectPage')
const toast = useToast()
const notificationsRef = ref<InstanceType<typeof NotificationsTab> | null>(null)

function onUpdate(value: Partial<ApplicationSettings>) {
  form.update({ ...form.settings.value, ...value } as ApplicationSettings)
}

// ─── Discord bot status ─────────────────────────────────────────────────────

const testingDiscord = ref(false)
const discordBotStatus = ref<'unknown' | 'checking' | 'running' | 'stopped' | 'error'>('unknown')
const discordTokenValid = ref<boolean | null>(null)
const canTestDiscord = computed(
  () =>
    !!(
      form.settings.value?.discordApplicationId &&
      form.settings.value?.discordBotToken &&
      (discordBotStatus.value === 'running' || discordTokenValid.value === true)
    ),
)

async function checkDiscordBotRunning() {
  discordBotStatus.value = 'checking'
  discordTokenValid.value = null
  try {
    const resp = await apiService.getDiscordBotStatus()
    discordBotStatus.value = resp?.success ? (resp.isRunning ? 'running' : 'stopped') : 'error'

    // With an app id and token configured, also validate the token and guild membership.
    if (form.settings.value?.discordApplicationId && form.settings.value?.discordBotToken) {
      try {
        const tokenResp = (await apiService.getDiscordStatus()) as {
          success?: boolean
          installed?: boolean
          botInfo?: unknown
        }
        // A configured guild answers with installed; without one a valid token answers with botInfo.
        discordTokenValid.value = !!(
          tokenResp?.success &&
          (tokenResp.installed === true || tokenResp.botInfo)
        )
      } catch {
        discordTokenValid.value = false
      }
    }
  } catch (err) {
    discordBotStatus.value = 'error'
    errorTracking.captureException(err as Error, {
      component: 'ConnectPage',
      operation: 'checkDiscordBotRunning',
    })
  }
}

async function testDiscordIntegration() {
  if (!canTestDiscord.value) {
    toast.error(
      'Cannot test',
      'Ensure Application ID and Bot Token are configured and the Discord bot is running',
    )
    return
  }

  testingDiscord.value = true
  try {
    const resp = await apiService.getDiscordStatus()
    if (resp?.success)
      toast.success('Discord test', resp.message || 'Discord integration appears configured')
    else toast.error('Discord test failed', resp?.message || 'Discord test failed')
  } catch (err) {
    errorTracking.captureException(err as Error, {
      component: 'ConnectPage',
      operation: 'testDiscordIntegration',
    })
    toast.error('Test failed', describeApiError(err, 'Discord test failed.'))
  } finally {
    testingDiscord.value = false
  }
}

let discordPollTimer: number | undefined

onMounted(async () => {
  await form.load()
  await checkDiscordBotRunning()
  discordPollTimer = window.setInterval(checkDiscordBotRunning, 30000)
})

onBeforeUnmount(() => {
  if (discordPollTimer) window.clearInterval(discordPollTimer)
})
</script>

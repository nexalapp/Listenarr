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
    title="Security"
    description="Who can log in, and the API key other tools use to talk to this server."
    :icon="PhShieldCheck"
    show-save
    :saving="form.isSaving.value"
    @save="save"
  >
    <div v-if="form.settings.value" class="settings-form">
      <AuthenticationSection
        :settings="form.settings.value"
        :apiKey="apiKey"
        v-model:authEnabled="authEnabled"
        @update:settings="onUpdate"
        @update:apiKey="(v: string) => (apiKey = v)"
      />
    </div>
  </SettingsPageShell>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { PhShieldCheck } from '@phosphor-icons/vue'
import SettingsPageShell from '@/views/settings/SettingsPageShell.vue'
import AuthenticationSection from '@/components/settings/AuthenticationSection.vue'
import { useApplicationSettingsForm } from '@/composables/useApplicationSettingsForm'
import { apiService } from '@/services/api'
import { useToast } from '@/services/toastService'
import { useAuthStore } from '@/stores/auth'
import { sessionTokenManager } from '@/utils/sessionToken'
import { logger } from '@/utils/logger'
import type { ApplicationSettings, StartupConfig } from '@/types'

const STARTUP_CONFIG_UPDATED_EVENT = 'listenarr-startup-config-updated'

const form = useApplicationSettingsForm('SecurityPage')
const toast = useToast()
const auth = useAuthStore()
const router = useRouter()

const apiKey = ref('')
const authEnabled = ref(false)
const startupConfig = ref<StartupConfig | null>(null)

function onUpdate(value: Partial<ApplicationSettings>) {
  form.update({ ...form.settings.value, ...value } as ApplicationSettings)
}

/** Whatever spelling config.json used, as a boolean. */
function readAuthRequired(config: StartupConfig | null): boolean {
  const obj = config as Record<string, unknown> | null
  const raw = obj ? (obj['authenticationRequired'] ?? obj['AuthenticationRequired']) : undefined
  if (typeof raw === 'boolean') return raw
  if (typeof raw === 'string') {
    const normalized = raw.toLowerCase().trim()
    return (
      normalized === 'enabled' ||
      normalized === 'true' ||
      normalized === 'yes' ||
      normalized === '1'
    )
  }
  return false
}

async function save() {
  const saved = await form.save((payload) => {
    // Empty admin fields mean "leave the account alone", not "set it to nothing".
    const next = { ...payload }
    if (!next.adminUsername?.trim()) delete next.adminUsername
    if (!next.adminPassword?.trim()) delete next.adminPassword
    return next
  })
  if (!saved) return
  await persistAuthToggle()
}

/**
 * The login requirement lives in config.json, not in the settings row, so it is
 * written separately. A server refusal (no admin user yet) is shown as such; a disk
 * failure offers the file for download so the operator can place it by hand.
 */
async function persistAuthToggle() {
  const wasEnabled = readAuthRequired(startupConfig.value)
  if (wasEnabled === authEnabled.value) return

  const rest = { ...(startupConfig.value ?? {}) } as Record<string, unknown>
  delete rest.AuthenticationRequired
  const next: StartupConfig = {
    ...rest,
    authenticationRequired: authEnabled.value ? 'true' : 'false',
  }

  try {
    await apiService.saveStartupConfig(next)
    startupConfig.value = next
    toast.success('Startup config', 'Startup configuration saved (config.json)')
  } catch (err) {
    const status = (err as { status?: number } | null)?.status
    if (typeof status === 'number' && status >= 400 && status < 500) {
      const message =
        err instanceof Error && err.message
          ? err.message
          : 'Startup configuration refused by the server.'
      toast.error('Startup config refused', message)
      authEnabled.value = wasEnabled
      return
    }
    toast.info(
      'Startup config',
      'Could not persist startup config to disk. Preparing a downloadable config.json so you can save it manually.',
    )
    try {
      const blob = new Blob([JSON.stringify(next, null, 2)], { type: 'application/json' })
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = 'config.json'
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    } catch {
      toast.info(
        'Startup config',
        'Edit config/config.json on the host to make the change persistent.',
      )
    }
    return
  }

  try {
    window.dispatchEvent(new Event(STARTUP_CONFIG_UPDATED_EVENT))
  } catch {}

  if (!authEnabled.value) {
    // Auth was just disabled: clear stale local session state immediately.
    try {
      sessionTokenManager.clearToken()
    } catch {}
    auth.user.authenticated = false
    auth.redirectTo = null
    try {
      await apiService.ensureAntiforgeryForCurrentAuth()
    } catch {}
    return
  }

  // Auth was just enabled: an unauthenticated session goes to login now, not on the
  // next navigation.
  try {
    await auth.loadCurrentUser()
  } catch {}
  if (!auth.user.authenticated) {
    const redirect = router.currentRoute.value.fullPath || '/settings/security'
    toast.info('Authentication enabled', 'Please log in to continue.')
    try {
      await router.push({ name: 'login', query: { redirect, force: '1' } })
    } catch {
      window.location.href = `/login?redirect=${encodeURIComponent(redirect)}&force=1`
    }
  }
}

onMounted(async () => {
  await form.load()
  try {
    startupConfig.value = await apiService.getStartupConfig()
    authEnabled.value = readAuthRequired(startupConfig.value)
  } catch {
    authEnabled.value = false
  }
  try {
    apiKey.value = (await apiService.getApiKey()).apiKey ?? ''
  } catch {
    apiKey.value = ''
  }
  try {
    const admins = await apiService.getAdminUsers()
    const first = admins[0]
    if (first && form.settings.value) {
      form.update({ ...form.settings.value, adminUsername: first.username })
    }
  } catch (e) {
    logger.debug('Failed to load admin users', e)
  }
})
</script>

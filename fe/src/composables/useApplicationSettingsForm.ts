/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
import { computed, ref } from 'vue'
import { useConfigurationStore } from '@/stores/configuration'
import { useToast } from '@/services/toastService'
import { errorTracking } from '@/services/errorTracking'
import { describeApiError } from '@/utils/apiError'
import type { ApplicationSettings } from '@/types'

/**
 * The application-settings form every settings page that edits `ApplicationSettings`
 * shares: load, hold a working copy, save once through the store so the singleton
 * row's concurrency version cannot race itself, and reload the authoritative copy
 * after a failed save so the next edit never carries a stale version.
 */
export function useApplicationSettingsForm(pageName: string) {
  const configStore = useConfigurationStore()
  const toast = useToast()
  const settings = ref<ApplicationSettings | null>(null)
  const loading = ref(false)
  const saving = ref(false)

  async function load() {
    loading.value = true
    try {
      const loaded = await configStore.loadApplicationSettings()
      settings.value = loaded ? normalize(loaded) : null
      if (settings.value) configStore.applicationSettings = settings.value
    } finally {
      loading.value = false
    }
  }

  function update(value: ApplicationSettings | null) {
    settings.value = value
    if (value) configStore.applicationSettings = value
  }

  /**
   * Save the working copy. Returns the saved settings, or null when the save failed
   * (the error has already been shown and the working copy reloaded).
   */
  async function save(
    prepare?: (payload: ApplicationSettings) => ApplicationSettings,
  ): Promise<ApplicationSettings | null> {
    if (!settings.value) return null
    saving.value = true
    try {
      const payload = prepare ? prepare({ ...settings.value }) : { ...settings.value }
      const saved = await configStore.saveApplicationSettings(payload)
      settings.value = saved
      toast.success('Settings', 'Settings saved successfully')
      return saved
    } catch (error) {
      errorTracking.captureException(error as Error, {
        component: pageName,
        operation: 'saveSettings',
      })
      // The backend preserves other settings if a later step fails after the row
      // commits, and a stale-version conflict means another writer advanced it.
      // Either way the working copy must be the server's before the next edit.
      const reloaded = await configStore.loadApplicationSettings()
      if (reloaded) settings.value = normalize(reloaded)
      toast.error('Save failed', describeError(error))
      return null
    } finally {
      saving.value = false
    }
  }

  return {
    settings,
    loading,
    saving,
    // Not the store's isLoading: that is also true while the page loads, and a Save
    // button that says "Saving..." before anything was touched is a lie.
    isSaving: computed(() => saving.value),
    load,
    update,
    save,
  }
}

/** Fill the defaults a row written by an older build may lack. */
function normalize(raw: ApplicationSettings): ApplicationSettings {
  const s = { ...raw }
  if (!s.completedFileAction) s.completedFileAction = 'copy'
  if (s.downloadCompletionStabilitySeconds == null) s.downloadCompletionStabilitySeconds = 10
  if (s.missingSourceRetryInitialDelaySeconds == null) s.missingSourceRetryInitialDelaySeconds = 30
  if (s.missingSourceMaxRetries == null) s.missingSourceMaxRetries = 3
  if (!s.enabledNotificationTriggers) s.enabledNotificationTriggers = []
  if (s.enableOpenLibrarySearch == null) s.enableOpenLibrarySearch = true
  if (!s.defaultSearchRegion?.trim()) s.defaultSearchRegion = 'us'
  if (!s.defaultSearchLanguage?.trim()) s.defaultSearchLanguage = 'english'
  return s
}

function describeError(error: unknown): string {
  if ((error as { status?: number } | null)?.status === 409) {
    return 'Someone else saved settings first. The latest values have been reloaded; make your change again.'
  }
  return describeApiError(error, 'Could not save settings.')
}

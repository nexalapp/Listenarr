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
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { apiService } from '@/services/api'
import type { AutoGrabKind, AutoGrabPlan, AutoGrabSettings, AutoGrabSource } from '@/types'

/**
 * Which Suggested lists are grabbed from without being asked.
 *
 * One store rather than a copy per component: the switch lives on the page header, the
 * ticks live on the lists themselves, and both edit the same stored document. Keeping
 * two copies of it in sync through props is how one of them ends up wrong.
 */
export const useAutoGrabStore = defineStore('autoGrab', () => {
  const settings = ref<AutoGrabSettings | null>(null)
  const plan = ref<AutoGrabPlan | null>(null)
  const loading = ref(false)
  const saving = ref(false)
  const error = ref<string | null>(null)

  const sources = computed<AutoGrabSource[]>(() => settings.value?.sources ?? [])
  const enabled = computed(() => settings.value?.enabled ?? false)

  function keyOf(kind: AutoGrabKind, sectionId?: string | null): string {
    return sectionId ? `${kind}:${sectionId}` : kind
  }

  /** Whether a list is ticked. An unknown list is not. */
  function includes(kind: AutoGrabKind, sectionId?: string | null): boolean {
    const key = keyOf(kind, sectionId)
    return sources.value.some(
      (source) => keyOf(source.kind, source.sectionId) === key && source.enabled,
    )
  }

  async function load(force = false): Promise<void> {
    if (settings.value && !force) return
    loading.value = true
    error.value = null
    try {
      settings.value = await apiService.getAutoGrabSettings()
    } catch {
      error.value = 'The auto-grab settings could not be read.'
    } finally {
      loading.value = false
    }
  }

  async function save(next: AutoGrabSettings): Promise<void> {
    saving.value = true
    try {
      settings.value = await apiService.saveAutoGrabSettings(next)
      // What a run would do has changed, so what is on screen is now a guess.
      plan.value = null
    } finally {
      saving.value = false
    }
  }

  /**
   * Ticks or unticks one list. A list ticked for the first time goes to the end of the
   * order, which is where someone adding a source would expect it: after the ones they
   * already chose.
   */
  async function toggle(kind: AutoGrabKind, sectionId?: string | null): Promise<void> {
    await load()
    const current = settings.value ?? {
      enabled: false,
      intervalMinutes: 60,
      sources: [],
    }
    const key = keyOf(kind, sectionId)
    const existing = current.sources.find((source) => keyOf(source.kind, source.sectionId) === key)

    const next: AutoGrabSettings = existing
      ? {
          ...current,
          sources: current.sources.map((source) =>
            keyOf(source.kind, source.sectionId) === key
              ? { ...source, enabled: !source.enabled }
              : source,
          ),
        }
      : {
          ...current,
          sources: [...current.sources, { kind, sectionId: sectionId ?? null, enabled: true }],
        }

    // Ticking a list is how someone turns auto-grab on: the master switch lives in the
    // panel, and a tick that leaves it off does nothing at all, with nothing on the page
    // to say why. Unticking the last list turns it back off, which is the same sentence
    // read backwards. The panel's switch still pauses it without disturbing the ticks.
    await save({ ...next, enabled: next.sources.some((source) => source.enabled) })
  }

  async function preview(): Promise<void> {
    loading.value = true
    try {
      plan.value = await apiService.getAutoGrabPreview()
    } catch {
      error.value = 'What a run would do could not be worked out.'
    } finally {
      loading.value = false
    }
  }

  /**
   * Set when something elsewhere on the page asks for the panel. The order of the
   * lists is set in the panel, and the tick that arms a list is on the tab, so without
   * this the two halves of one decision sit in places that cannot reach each other.
   */
  const panelRequested = ref(false)

  function openPanel(): void {
    panelRequested.value = true
  }

  function panelOpened(): void {
    panelRequested.value = false
  }

  return {
    panelRequested,
    openPanel,
    panelOpened,
    settings,
    plan,
    loading,
    saving,
    error,
    sources,
    enabled,
    includes,
    load,
    save,
    toggle,
    preview,
  }
})

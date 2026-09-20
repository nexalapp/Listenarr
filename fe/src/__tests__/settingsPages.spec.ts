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
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { apiService } from '@/services/api'

vi.mock('@/services/api', () => ({
  apiService: {
    getApplicationSettings: vi.fn(async () => ({})),
    saveApplicationSettings: vi.fn(async (s: unknown) => s),
    getStartupConfig: vi.fn(async () => ({})),
    saveStartupConfig: vi.fn(async () => ({})),
    getApiKey: vi.fn(async () => ({ apiKey: 'abc' })),
    getAdminUsers: vi.fn(async () => []),
    getRootFolders: vi.fn(async () => []),
    getDownloadClientConfigurations: vi.fn(async () => []),
    getRemotePathMappings: vi.fn(async () => []),
    getBootstrapConfig: vi.fn(async () => ({})),
    generateInitialApiKey: vi.fn(async () => ({ apiKey: 'abc' })),
    regenerateApiKey: vi.fn(async () => ({ apiKey: 'abc' })),
    ensureAntiforgeryForCurrentAuth: vi.fn(async () => undefined),
  },
}))

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: { template: '<div />' } },
      { path: '/login', name: 'login', component: { template: '<div />' } },
    ],
  })
}

describe('settings pages', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
  })

  it('Media Management saves a child edit once, carrying the loaded version', async () => {
    ;(apiService.getApplicationSettings as Mock).mockResolvedValue({
      version: 7,
      folderNamingPattern: '{Author}/{Series}/{Title}',
      fileNamingPattern: '{Title}',
    })
    const { default: Page } = await import('@/views/settings/pages/MediaManagementPage.vue')
    const router = makeRouter()
    await router.push('/')
    const wrapper = mount(Page, {
      global: { plugins: [createPinia(), router], stubs: ['FolderBrowser', 'RootFoldersTab'] },
    })
    const input = await vi.waitFor(() => {
      const found = wrapper.find('input[placeholder="{Title}"]')
      expect(found.exists()).toBe(true)
      return found
    })
    await input.setValue('{Title}-{DiskNumber}')

    const { useConfigurationStore } = await import('@/stores/configuration')
    const store = useConfigurationStore()
    store.saveApplicationSettings = vi
      .fn()
      .mockImplementation(async (payload) => ({ ...payload, version: 8 }))

    const save = wrapper.findAll('button').find((b) => b.text().includes('Save'))
    expect(save).toBeTruthy()
    await save!.trigger('click')

    expect(store.saveApplicationSettings).toHaveBeenCalledTimes(1)
    const payload = (store.saveApplicationSettings as Mock).mock.calls[0][0]
    expect(payload.fileNamingPattern).toBe('{Title}-{DiskNumber}')
    expect(payload.version).toBe(7)
  })

  it('reloads the authoritative version after a failed save', async () => {
    ;(apiService.getApplicationSettings as Mock)
      .mockResolvedValueOnce({ version: 7, fileNamingPattern: '{Title}' })
      .mockResolvedValueOnce({ version: 9, fileNamingPattern: '{Title}' })
    const { default: Page } = await import('@/views/settings/pages/MediaManagementPage.vue')
    const router = makeRouter()
    await router.push('/')
    const wrapper = mount(Page, {
      global: { plugins: [createPinia(), router], stubs: ['FolderBrowser', 'RootFoldersTab'] },
    })
    await vi.waitFor(() => expect(wrapper.find('input[placeholder="{Title}"]').exists()).toBe(true))

    const { useConfigurationStore } = await import('@/stores/configuration')
    const store = useConfigurationStore()
    store.saveApplicationSettings = vi
      .fn()
      .mockRejectedValue(Object.assign(new Error('stale'), { status: 409 }))

    const save = wrapper.findAll('button').find((b) => b.text().includes('Save'))
    await save!.trigger('click')

    await vi.waitFor(() => {
      expect(store.applicationSettings?.version).toBe(9)
    })
  })

  it('Security reads the login requirement from the startup config', async () => {
    ;(apiService.getStartupConfig as Mock).mockResolvedValue({ AuthenticationRequired: 'Enabled' })
    const { default: Page } = await import('@/views/settings/pages/SecurityPage.vue')
    const router = makeRouter()
    await router.push('/')
    const wrapper = mount(Page, { global: { plugins: [createPinia(), router] } })

    const vm = wrapper.vm as unknown as { authEnabled: boolean }
    await vi.waitFor(() => expect(vm.authEnabled).toBe(true))
  })

  it('Security writes the login requirement to the startup config only when it changed', async () => {
    ;(apiService.getStartupConfig as Mock).mockResolvedValue({ authenticationRequired: 'false' })
    ;(apiService.getApplicationSettings as Mock).mockResolvedValue({ version: 1 })
    const { default: Page } = await import('@/views/settings/pages/SecurityPage.vue')
    const router = makeRouter()
    await router.push('/')
    const wrapper = mount(Page, { global: { plugins: [createPinia(), router] } })
    const vm = wrapper.vm as unknown as { authEnabled: boolean }
    // The sections render once the settings have loaded; the Save button is there earlier.
    await vi.waitFor(() => expect(wrapper.find('.form-section').exists()).toBe(true))

    const save = wrapper.findAll('button').find((b) => b.text() === 'Save')
    await save!.trigger('click')
    await vi.waitFor(() => expect(apiService.saveApplicationSettings).toHaveBeenCalledTimes(1))
    expect(apiService.saveStartupConfig).not.toHaveBeenCalled()

    vm.authEnabled = true
    await save!.trigger('click')
    await vi.waitFor(() => expect(apiService.saveStartupConfig).toHaveBeenCalledTimes(1))
    expect((apiService.saveStartupConfig as Mock).mock.calls[0][0]).toMatchObject({
      authenticationRequired: 'true',
    })
  })
})

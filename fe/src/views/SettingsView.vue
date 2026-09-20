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
  <div class="settings-page">
    <RouterView />
  </div>
</template>

<script setup lang="ts">
// Settings is a set of pages, one per job (see router: /settings/<page>), reached from
// the sidebar's Settings sub-navigation. This shell only frames whichever page is open.
</script>

<style scoped>
.settings-page {
  padding: 2rem;
  background-color: #1a1a1a;
  min-height: 100%;
}

/* Every page shares the section/card look the settings forms were built around. */
.settings-page :deep(.settings-form) {
  display: flex;
  flex-direction: column;
  gap: 2rem;
}

.settings-page :deep(.form-section) {
  background: transparent;
  border: none;
  padding: 0;
}

.settings-page :deep(.form-section > h3) {
  margin: 0 0 0.75rem 0;
  padding: 0;
  font-size: 1.1rem;
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: #fff;
}

.settings-page :deep(.form-section .form-body) {
  padding: 1.25rem;
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.03);
  box-shadow: 0 6px 20px rgba(0, 0, 0, 0.7);
  background: #0b0b0b;
}

.settings-page :deep(.form-section .form-body .form-group + .form-group) {
  margin-top: 0.85rem;
}

/* Consistent ordering for card actions: edit -> secondary -> delete */
.settings-page :deep(.action-edit) {
  order: 1;
}
.settings-page :deep(.action-secondary) {
  order: 2;
}
.settings-page :deep(.action-delete) {
  order: 3;
}
.settings-page :deep(.folder-actions),
.settings-page :deep(.mapping-actions),
.settings-page :deep(.indexer-actions),
.settings-page :deep(.config-actions) {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}

@media (max-width: 768px) {
  .settings-page {
    padding: 1rem;
  }
}
</style>

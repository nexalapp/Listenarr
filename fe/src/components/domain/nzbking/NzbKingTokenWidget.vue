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
  <!-- Absent entirely when no key is configured: there is no allowance to report. -->
  <div
    v-if="tokens.status?.configured"
    class="token-widget"
    :class="{ attention: tokens.needsAttention }"
  >
    <div class="token-head">
      <span class="token-title">NZBKing tokens</span>
      <span v-if="tokens.status.keyDeleted" class="token-count deleted">key deleted</span>
      <!-- "About" rather than a bare figure: NZBKing publishes no balance, so this is
           our own reckoning and can drift from theirs. -->
      <span v-else class="token-count"
        >≈{{ tokens.estimatedBalance }} of {{ tokens.status.maxTokens }}</span
      >
    </div>

    <div
      v-if="!tokens.status.keyDeleted"
      class="token-bar"
      role="img"
      :aria-label="`About ${tokens.estimatedBalance} of ${tokens.status.maxTokens} tokens, ${tokens.spendable} spendable before the reserve of ${tokens.status.reserveFloor}`"
    >
      <div class="token-bar-spendable" :style="{ width: spendablePercent + '%' }"></div>
      <!-- The reserve is drawn rather than merely subtracted, because the number that
           matters is how many can be spent before the floor, not how many exist. -->
      <div class="token-bar-reserve" :style="{ width: reservePercent + '%' }"></div>
    </div>

    <p v-if="tokens.status.keyDeleted" class="token-note deleted">
      Request a new key and save it against the abook.link source.
    </p>
    <p v-else class="token-note">{{ tokens.spendable }} spendable · {{ refillText }}</p>

    <p v-if="activityText" class="token-activity">{{ activityText }}</p>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount } from 'vue'
import { useNzbKingTokensStore } from '@/stores/nzbKingTokens'

const tokens = useNzbKingTokensStore()

const spendablePercent = computed(() => {
  const s = tokens.status
  if (!s?.configured || s.maxTokens <= 0) return 0
  return Math.max(0, Math.min(100, (tokens.spendable / s.maxTokens) * 100))
})

const reservePercent = computed(() => {
  const s = tokens.status
  if (!s?.configured || s.maxTokens <= 0) return 0
  const held = Math.max(0, tokens.estimatedBalance - tokens.spendable)
  return Math.max(0, Math.min(100, (held / s.maxTokens) * 100))
})

const refillText = computed(() => {
  const ms = tokens.msUntilNextRefill
  if (ms === null) return 'full'

  const minutes = Math.max(1, Math.round(ms / 60000))
  return minutes >= 60 ? `+1 in ${Math.floor(minutes / 60)}h ${minutes % 60}m` : `+1 in ${minutes}m`
})

const activityText = computed(() => {
  const s = tokens.status
  if (!s?.configured) return ''

  const parts: string[] = []
  if (s.spentRecently > 0) parts.push(`${s.spentRecently} spent`)
  // Refusals are worth naming: that is the budget protecting the key, not a fault.
  if (s.refusedRecently > 0) parts.push(`${s.refusedRecently} refused`)
  return parts.length > 0 ? `Last 24h: ${parts.join(', ')}` : ''
})

onMounted(() => {
  tokens.start()
  void tokens.load()
})

onBeforeUnmount(() => {
  tokens.stop()
})
</script>

<style scoped>
.token-widget {
  padding: 0.75rem 1rem;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.token-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 0.5rem;
}

.token-title {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: #868e96;
}

.token-count {
  font-size: 0.8rem;
  font-weight: 600;
  color: #e9ecef;
  font-variant-numeric: tabular-nums;
}

.token-count.deleted,
.token-note.deleted {
  color: #ff6b6b;
}

.token-bar {
  display: flex;
  height: 6px;
  border-radius: 3px;
  overflow: hidden;
  background: rgba(255, 255, 255, 0.08);
}

.token-bar-spendable {
  background: #4dabf7;
}

.token-bar-reserve {
  background: rgba(255, 212, 59, 0.5);
}

.attention .token-bar-spendable {
  background: #ffa94d;
}

.token-note,
.token-activity {
  margin: 0;
  font-size: 0.72rem;
  color: #adb5bd;
  font-variant-numeric: tabular-nums;
}

.token-activity {
  color: #868e96;
}
</style>

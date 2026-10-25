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
      <span v-if="tokens.status.keyDeleted" class="token-count deleted">{{ deletedLabel }}</span>
      <!-- The spendable count, not the raw balance. The reserve is never spendable, so
           quoting the total invites reconciling two numbers to reach the one that
           actually governs whether a grab can happen. The bar still shows the reserve.
           "About" because NZBKing publishes no balance: this is our own reckoning. -->
      <span v-else class="token-count">≈{{ tokens.spendable }} available</span>
    </div>

    <div
      v-if="!tokens.status.keyDeleted"
      class="token-bar"
      role="img"
      :aria-label="`About ${tokens.estimatedBalance} of ${tokens.poolMaxTokens} tokens across ${liveKeyCount} ${liveKeyCount === 1 ? 'key' : 'keys'}, ${tokens.spendable} spendable before the reserve of ${tokens.status.reserveFloor} per key`"
    >
      <div class="token-bar-spendable" :style="{ width: spendablePercent + '%' }"></div>
      <!-- The reserve is drawn rather than merely subtracted, because the number that
           matters is how many can be spent before the floor, not how many exist. -->
      <div class="token-bar-reserve" :style="{ width: reservePercent + '%' }"></div>
    </div>

    <p v-if="tokens.status.keyDeleted" class="token-note deleted">
      <a :href="KEY_PAGE" target="_blank" rel="noopener noreferrer">{{
        keyCount > 1 ? 'Request new keys' : 'Request a new key'
      }}</a>
      and save {{ keyCount > 1 ? 'them' : 'it' }} against the abook.link source.
    </p>
    <template v-else>
      <!-- Only when a token is actually due. At the maximum there is nothing to wait
           for, and saying so takes a word that reads as a third competing figure. -->
      <p v-if="refillText" class="token-note">{{ refillText }}</p>

      <!-- Only with a pool: with one key the count is already implied, and with none the
           line above says what to do. -->
      <p v-if="keyCount > 1" class="token-note">across {{ keyCount }} keys</p>

      <!-- A dead key among live ones is easy to miss, because the pool goes on working
           without it and nothing in the balance says it is now smaller. -->
      <p v-if="deadKeyCount > 0" class="token-note deleted">
        {{ deadKeyCount }} of them {{ deadKeyCount === 1 ? 'has' : 'have' }} been deleted.
        <a :href="KEY_PAGE" target="_blank" rel="noopener noreferrer">Request a replacement</a>.
      </p>
    </template>

    <!-- A refusal is an event, not a level: a grab was asked for and did not happen.
         The balance cannot show that, and the toast that announced it is long gone.
         How many were spent needs no line -- the count above already reflects them. -->
    <p v-if="refusedText" class="token-activity">{{ refusedText }}</p>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount } from 'vue'
import { useNzbKingTokensStore } from '@/stores/nzbKingTokens'

const tokens = useNzbKingTokensStore()

/**
 * Where a key comes from. NZBKing issues one behind a CAPTCHA, so replacing a dead
 * key is a person's job -- the least this can do is be one click away from the form.
 */
const KEY_PAGE = 'https://nzbking.com/rss/'

const keyCount = computed(() => tokens.status?.keyCount ?? 0)
const deadKeyCount = computed(() => tokens.status?.deletedKeyCount ?? 0)
const liveKeyCount = computed(() => Math.max(0, keyCount.value - deadKeyCount.value))

const deletedLabel = computed(() =>
  keyCount.value > 1 ? `all ${keyCount.value} keys deleted` : 'key deleted',
)

// Drawn against the live keys' combined ceiling: a deleted key contributes no tokens
// and no headroom, so counting its 100 would show a full pool as half empty.
const spendablePercent = computed(() => {
  if (tokens.poolMaxTokens <= 0) return 0
  return Math.max(0, Math.min(100, (tokens.spendable / tokens.poolMaxTokens) * 100))
})

const reservePercent = computed(() => {
  if (tokens.poolMaxTokens <= 0) return 0
  const held = Math.max(0, tokens.estimatedBalance - tokens.spendable)
  return Math.max(0, Math.min(100, (held / tokens.poolMaxTokens) * 100))
})

const refillText = computed(() => {
  const ms = tokens.msUntilNextRefill
  if (ms === null) return ''

  const minutes = Math.max(1, Math.round(ms / 60000))
  return minutes >= 60 ? `+1 in ${Math.floor(minutes / 60)}h ${minutes % 60}m` : `+1 in ${minutes}m`
})

const refusedText = computed(() => {
  const refused = tokens.status?.refusedRecently ?? 0
  if (refused < 1) return ''
  return refused === 1
    ? '1 grab refused in the last 24h'
    : `${refused} grabs refused in the last 24h`
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

.token-note a {
  color: inherit;
  text-decoration: underline;
}
</style>

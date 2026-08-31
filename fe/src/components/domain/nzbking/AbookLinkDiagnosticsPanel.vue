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
  <div class="abook-panel">
    <div class="abook-panel-head">
      <h4>abook.link diagnostics</h4>
      <button class="btn btn-secondary btn-sm" :disabled="testing" @click="testSignIn">
        {{ testing ? 'Testing…' : 'Test sign-in' }}
      </button>
    </div>

    <!-- Sign-in is the failure that masquerades as everything else: the forum serves
         a logged-out page with HTTP 200, so a rejected session looks like an empty
         result rather than an error. The probe reports each page separately. -->
    <div
      v-if="signIn"
      class="abook-signin"
      :class="{ ok: signInLooksHealthy, bad: !signInLooksHealthy }"
    >
      <p class="abook-signin-verdict">
        {{ signInLooksHealthy ? 'Signed in.' : 'Not signed in.' }}
      </p>
      <dl class="abook-kv">
        <template v-for="(value, key) in signIn" :key="key">
          <dt>{{ key }}</dt>
          <dd>{{ value }}</dd>
        </template>
      </dl>
    </div>

    <div class="abook-ledger">
      <div class="abook-panel-head">
        <h4>NZBKing access log</h4>
        <button class="btn btn-secondary btn-sm" :disabled="loadingLedger" @click="loadLedger">
          {{ loadingLedger ? 'Loading…' : 'Refresh' }}
        </button>
      </div>

      <p v-if="!ledger.length" class="abook-empty">
        No NZBKing calls recorded yet. Entries appear when a grab needs NZBKing, which only happens
        after the free indexes have been tried.
      </p>

      <table v-else class="abook-table">
        <thead>
          <tr>
            <th>When</th>
            <th>Purpose</th>
            <th>Outcome</th>
            <th>Query</th>
            <th class="numeric">Left</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(entry, index) in ledger" :key="index">
            <td>{{ formatWhen(entry.attemptedAt) }}</td>
            <td>{{ entry.purpose }}</td>
            <td>
              <span class="outcome" :class="outcomeClass(entry.outcome)">{{
                describeOutcome(entry.outcome)
              }}</span>
            </td>
            <td class="query" :title="entry.query || ''">{{ entry.query || '—' }}</td>
            <td class="numeric">{{ entry.balanceAfter }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { apiService } from '@/services/api'
import type { NzbKingAccess } from '@/types'
import { logger } from '@/utils/logger'

const signIn = ref<Record<string, string> | null>(null)
const testing = ref(false)
const ledger = ref<NzbKingAccess[]>([])
const loadingLedger = ref(false)

// The probe reports a logout link on pages a signed-in account can see. Its absence
// everywhere is what a rejected session looks like, since the status stays 200.
const signInLooksHealthy = computed(() => {
  const report = signIn.value
  if (!report) return false
  if (report.error) return false
  return Object.entries(report).some(
    ([key, value]) => key.endsWith('.hasLogoutLink') && value === 'yes',
  )
})

const testSignIn = async () => {
  testing.value = true
  try {
    signIn.value = await apiService.diagnoseAbookLogin()
  } catch (error) {
    logger.warn('[abook.link] Sign-in diagnostic failed', error)
    signIn.value = { error: 'The diagnostic could not be run. See the application log.' }
  } finally {
    testing.value = false
  }
}

const loadLedger = async () => {
  loadingLedger.value = true
  try {
    ledger.value = (await apiService.getNzbKingLedger(25)).entries
  } catch (error) {
    logger.warn('[abook.link] Failed to load the NZBKing ledger', error)
  } finally {
    loadingLedger.value = false
  }
}

const describeOutcome = (outcome: string) => {
  switch (outcome) {
    // Named for what happened rather than echoing the enum: a refusal is the budget
    // working, not a failure, and the two read very differently to an operator.
    case 'DeniedByBudget':
      return 'refused (reserve)'
    case 'Spent':
      return 'spent'
    case 'KeyDeleted':
      return 'key deleted'
    case 'Failed':
      return 'failed'
    default:
      return outcome
  }
}

const outcomeClass = (outcome: string) =>
  outcome === 'Spent' ? 'ok' : outcome === 'DeniedByBudget' ? 'warn' : 'bad'

const formatWhen = (iso: string) => {
  const at = new Date(iso)
  return Number.isNaN(at.getTime()) ? iso : at.toLocaleString()
}

onMounted(() => {
  void loadLedger()
})
</script>

<style scoped>
.abook-panel {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 1rem;
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.02);
}

.abook-panel-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.abook-panel-head h4 {
  margin: 0;
  font-size: 0.85rem;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: #868e96;
}

.abook-signin {
  padding: 0.75rem;
  border-radius: 6px;
  border-left: 3px solid;
}

.abook-signin.ok {
  border-color: #51cf66;
  background: rgba(81, 207, 102, 0.06);
}

.abook-signin.bad {
  border-color: #ff6b6b;
  background: rgba(255, 107, 107, 0.06);
}

.abook-signin-verdict {
  margin: 0 0 0.5rem;
  font-weight: 600;
}

.abook-kv {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.15rem 0.75rem;
  margin: 0;
  font-size: 0.75rem;
  font-variant-numeric: tabular-nums;
}

.abook-kv dt {
  color: #868e96;
}

.abook-kv dd {
  margin: 0;
  overflow-wrap: anywhere;
}

.abook-empty {
  margin: 0;
  font-size: 0.8rem;
  color: #868e96;
}

.abook-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.75rem;
}

.abook-table th {
  text-align: left;
  padding: 0.35rem 0.5rem;
  color: #868e96;
  font-weight: 600;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.abook-table td {
  padding: 0.35rem 0.5rem;
  border-bottom: 1px solid rgba(255, 255, 255, 0.04);
}

.abook-table .numeric {
  text-align: right;
  font-variant-numeric: tabular-nums;
}

.abook-table .query {
  max-width: 22ch;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.outcome.ok {
  color: #51cf66;
}

.outcome.warn {
  color: #ffd43b;
}

.outcome.bad {
  color: #ff6b6b;
}
</style>

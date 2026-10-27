<!--
  Listenarr - Audiobook Management System
  Copyright (C) 2024-2026 Listenarr Contributors

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU Affero General Public License as published
  by the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.
-->
<template>
  <section class="tasks">
    <header class="tasks-head">
      <div>
        <h1>Tasks</h1>
        <p>
          Every background loop, when it last ran and when it will run again. Times are
          for this run of the application: a restart starts the record over.
        </p>
      </div>
      <button type="button" class="tasks-refresh" :disabled="loading" @click="load">
        Refresh
      </button>
    </header>

    <LoadingState v-if="loading && !tasks.length" message="Reading the schedule…" />

    <p v-else-if="error" class="tasks-error">{{ error }}</p>

    <table v-else class="tasks-table">
      <thead>
        <tr>
          <th>Name</th>
          <th>Interval</th>
          <th>Last run</th>
          <th>Took</th>
          <th>Next run</th>
          <th><span class="sr-only">Run now</span></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="task in tasks" :key="task.name" :class="{ running: task.running }">
          <td class="tasks-name">
            {{ task.name }}
            <span v-if="task.lastOutcome === 'Failed'" class="tasks-failed">last run failed</span>
          </td>
          <td>{{ duration(task.intervalSeconds) }}</td>
          <td>{{ task.lastStartedAt ? ago(task.lastStartedAt) : 'never this run' }}</td>
          <td>{{ task.lastDurationSeconds === null ? '—' : took(task.lastDurationSeconds) }}</td>
          <td>
            <span v-if="task.running">running now</span>
            <span v-else-if="task.nextDueAt">{{ until(task.nextDueAt) }}</span>
            <span v-else>—</span>
          </td>
          <td class="tasks-run">
            <button
              type="button"
              :disabled="task.running || waking === task.name"
              :title="`Wake ${task.name} so its next cycle starts now`"
              @click="run(task.name)"
            >
              Run now
            </button>
          </td>
        </tr>
      </tbody>
    </table>

    <p class="tasks-note">
      Run now wakes a sleeping loop; it does not overrule a task that keeps an interval of
      its own. Auto-grab counts an hour between runs whoever asked, so waking it will have
      it look at its own clock and go back to sleep — use Run now on the auto-grab panel
      for that one.
    </p>
  </section>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { LoadingState } from '@/components/base'
import { apiService } from '@/services/api'
import { useToast } from '@/services/toastService'
import { describeApiError } from '@/utils/apiError'
import type { BackgroundTask } from '@/types'

const toast = useToast()
const tasks = ref<BackgroundTask[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const waking = ref<string | null>(null)
let timer: ReturnType<typeof setInterval> | null = null

async function load() {
  loading.value = true
  try {
    tasks.value = await apiService.getBackgroundTasks()
    error.value = null
  } catch (e) {
    error.value = describeApiError(e, 'The task schedule could not be read.')
  } finally {
    loading.value = false
  }
}

async function run(name: string) {
  waking.value = name
  try {
    await apiService.runBackgroundTask(name)
    toast.success('Woken', `${name} will start its next cycle now.`)
    await load()
  } catch (e) {
    toast.error('Not woken', describeApiError(e, `${name} could not be woken.`))
  } finally {
    waking.value = null
  }
}

/** "5 minutes", "6 hours" - the interval as a person would say it. */
function duration(seconds: number): string {
  if (seconds < 60) return `${seconds} seconds`
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return minutes === 1 ? 'a minute' : `${minutes} minutes`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return hours === 1 ? 'an hour' : `${hours} hours`
  const days = Math.round(hours / 24)
  return days === 1 ? 'a day' : `${days} days`
}

function took(seconds: number): string {
  if (seconds < 1) return 'under a second'
  if (seconds < 60) return `${seconds.toFixed(1)}s`
  return duration(Math.round(seconds))
}

function ago(iso: string): string {
  const seconds = Math.max(0, (Date.now() - Date.parse(iso)) / 1000)
  return seconds < 10 ? 'just now' : `${duration(Math.round(seconds))} ago`
}

function until(iso: string): string {
  const seconds = (Date.parse(iso) - Date.now()) / 1000
  return seconds <= 5 ? 'any moment' : `in ${duration(Math.round(seconds))}`
}

onMounted(() => {
  load()
  // The point of the page is watching a wait tick down, so it refreshes itself.
  timer = setInterval(load, 10_000)
})

onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})
</script>

<style scoped>
.tasks {
  padding: 1.5rem;
}

.tasks-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1.25rem;
}

.tasks-head h1 {
  margin: 0 0 0.25rem;
  font-size: 1.4rem;
}

.tasks-head p {
  margin: 0;
  max-width: 54ch;
  opacity: 0.75;
  font-size: 0.9rem;
}

.tasks-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.92rem;
}

.tasks-table th {
  text-align: left;
  font-weight: 600;
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid rgba(128, 128, 128, 0.35);
  opacity: 0.8;
}

.tasks-table td {
  padding: 0.55rem 0.75rem;
  border-bottom: 1px solid rgba(128, 128, 128, 0.15);
}

.tasks-table tr.running td {
  font-weight: 600;
}

.tasks-name {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.tasks-failed {
  font-size: 0.78rem;
  color: #d98b8b;
  font-weight: 400;
}

.tasks-run {
  text-align: right;
}

.tasks-note {
  margin-top: 1rem;
  max-width: 70ch;
  font-size: 0.85rem;
  opacity: 0.7;
}

.tasks-error {
  color: #d98b8b;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
}
</style>

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
import { describe, expect, it } from 'vitest'
import { activityRank, sortActivity } from '@/utils/activityOrder'

const at = (minutesAgo: number) => new Date(Date.now() - minutesAgo * 60_000).toISOString()

describe('activityOrder', () => {
  it('ranks running, then queued, then needing attention, then done', () => {
    expect(activityRank('processing')).toBe(0)
    expect(activityRank('downloading')).toBe(0)
    expect(activityRank('moving')).toBe(0)
    expect(activityRank('queued')).toBe(1)
    expect(activityRank('importblocked')).toBe(2)
    expect(activityRank('failed')).toBe(2)
    expect(activityRank('completed')).toBe(3)
    expect(activityRank(undefined)).toBe(1)
  })

  it('puts the row being worked on first and the queue below it in the order it will run', () => {
    // As the API lists them: newest first, the running (oldest) one last.
    const items = [
      { id: 'c', status: 'queued', addedAt: at(1) },
      { id: 'b', status: 'queued', addedAt: at(5) },
      { id: 'a', status: 'processing', addedAt: at(10) },
    ]

    expect(sortActivity(items).map((i) => i.id)).toEqual(['a', 'b', 'c'])
  })

  it('shows failures after the queue, newest first, and finished work last', () => {
    const items = [
      { id: 'done-old', status: 'completed', addedAt: at(60) },
      { id: 'fail-old', status: 'importblocked', addedAt: at(30) },
      { id: 'next', status: 'queued', addedAt: at(2) },
      { id: 'fail-new', status: 'failed', addedAt: at(3) },
      { id: 'done-new', status: 'completed', addedAt: at(4) },
      { id: 'now', status: 'downloading', addedAt: at(9) },
    ]

    expect(sortActivity(items).map((i) => i.id)).toEqual([
      'now',
      'next',
      'fail-new',
      'fail-old',
      'done-new',
      'done-old',
    ])
  })

  it('keeps rows without a time after timed rows of the same rank, in arrival order', () => {
    const items = [
      { id: 'move-b', status: 'moving', addedAt: '' },
      { id: 'move-a', status: 'moving', addedAt: '' },
      { id: 'conv', status: 'processing', addedAt: at(1) },
    ]

    expect(sortActivity(items).map((i) => i.id)).toEqual(['conv', 'move-b', 'move-a'])
  })
})

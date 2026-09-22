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
import {
  findAlternatePublications,
  seriesPositionKey,
  storyTitleKey,
} from '@/utils/alternatePublications'

const book = (key: string, inLibrary: boolean, title: string, seriesNumber?: string | null) => ({
  key,
  inLibrary,
  title,
  seriesNumber,
})

describe('alternatePublications', () => {
  it('strips the packaging two publications of one story disagree about', () => {
    expect(storyTitleKey('Wool (Unabridged)')).toBe('wool')
    expect(storyTitleKey('Wool: The Silo Saga')).toBe('wool')
    expect(storyTitleKey('Shift — A Novel')).toBe('shift')
    expect(storyTitleKey('Dust: Special Edition')).toBe('dust')
    expect(storyTitleKey('')).toBe('')
  })

  it('reads a series position, and refuses an omnibus range', () => {
    expect(seriesPositionKey('3')).toBe('3')
    expect(seriesPositionKey(' 2.5 ')).toBe('2.5')
    expect(seriesPositionKey('03')).toBe('3')
    expect(seriesPositionKey('1-3')).toBeNull()
    expect(seriesPositionKey('Book One')).toBeNull()
    expect(seriesPositionKey(null)).toBeNull()
  })

  it('sets aside a catalogue book that sits where a held book sits', () => {
    const items = [
      book('lib-wool', true, 'Wool', '1'),
      book('lib-shift', true, 'Shift', '2'),
      book('cat-wool-tv', false, 'Wool: The Silo Saga', '1'),
      book('cat-dust', false, 'Dust', '3'),
    ]

    const alternates = findAlternatePublications(items, 'Silo')

    expect([...alternates]).toEqual(['cat-wool-tv'])
  })

  it('matches on the title when the catalogue gives no position', () => {
    const items = [
      book('lib-wool', true, 'Wool', '1'),
      book('cat-wool-again', false, 'Wool (Unabridged)', null),
    ]

    expect([...findAlternatePublications(items, 'Silo')]).toEqual(['cat-wool-again'])
  })

  it('works off titles alone outside a series, where a position means nothing', () => {
    const items = [
      book('lib', true, 'Project Hail Mary', '1'),
      // Another author's book that happens to be first in its own series.
      book('cat-other', false, 'The Martian', '1'),
      book('cat-same', false, 'Project Hail Mary: A Novel', '4'),
    ]

    expect([...findAlternatePublications(items, null)]).toEqual(['cat-same'])
  })

  it('leaves everything alone when the library holds none of it', () => {
    const items = [book('a', false, 'Wool', '1'), book('b', false, 'Wool (Unabridged)', '1')]

    expect(findAlternatePublications(items, 'Silo').size).toBe(0)
  })

  it('never sets aside a book the library holds', () => {
    const items = [
      book('lib-one', true, 'Wool', '1'),
      // A second copy of the same story, also owned: both stay.
      book('lib-two', true, 'Wool: The Silo Saga', '1'),
    ]

    expect(findAlternatePublications(items, 'Silo').size).toBe(0)
  })

  it('does not set aside an omnibus that spans books it happens to hold', () => {
    const items = [
      book('lib-1', true, 'Wool', '1'),
      book('lib-2', true, 'Shift', '2'),
      book('cat-omnibus', false, 'The Silo Trilogy', '1-3'),
    ]

    expect(findAlternatePublications(items, 'Silo').size).toBe(0)
  })
})

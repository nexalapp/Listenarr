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
import { describe, it, expect } from 'vitest'
import { isAudioAuditFlagged, isAudioAuditOverrulable } from '@/utils/audioAudit'

/**
 * The Tags page and a book's own page each had their own copy of this list. When "nothing
 * was heard" became `inconclusive` rather than `mismatch`, only the Tags page was told: the
 * books stayed there as problems and lost the button that says the record is right, so
 * there was no way to answer them. One list, and a test over every verdict.
 */
describe('audio audit flagging', () => {
  it('flags every verdict a book can be answered for', () => {
    expect(isAudioAuditFlagged('mismatch', false)).toBe(true)
    expect(isAudioAuditFlagged('narrator-mismatch', false)).toBe(true)
    expect(isAudioAuditFlagged('incomplete', false)).toBe(true)

    // A book nothing could be heard on is still waiting for an answer, and the answer may
    // well be that someone listened and it is fine.
    expect(isAudioAuditFlagged('inconclusive', false)).toBe(true)
  })

  it('does not flag a match, or a book nobody has listened to', () => {
    expect(isAudioAuditFlagged('match', false)).toBe(false)
    expect(isAudioAuditFlagged(null, false)).toBe(false)
    expect(isAudioAuditFlagged(undefined, false)).toBe(false)
  })

  it('stops flagging once it has been overruled', () => {
    expect(isAudioAuditFlagged('mismatch', true)).toBe(false)
    expect(isAudioAuditFlagged('inconclusive', true)).toBe(false)
  })

  it('offers the overrule for an accepted book too, so it can be withdrawn', () => {
    // The button becomes "Flag it again" rather than disappearing.
    expect(isAudioAuditOverrulable('inconclusive')).toBe(true)
    expect(isAudioAuditOverrulable('match')).toBe(false)
  })
})

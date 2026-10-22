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
import type { AudioAuditVerdict } from '@/types'

/**
 * Which verdicts mean the audit is flagging a book, in one place.
 *
 * The Tags page and a book's own page each kept their own copy of this list, and when
 * "nothing was heard" became `inconclusive` rather than `mismatch` only one of them was
 * told. The books stayed on the Tags page as problems and lost the button that says the
 * record is right, so there was no way to answer them.
 */
const FLAGGING: readonly AudioAuditVerdict[] = [
  'mismatch',
  'narrator-mismatch',
  'incomplete',
  'inconclusive',
]

/**
 * Whether the audit is flagging this book and nobody has overruled it.
 *
 * `inconclusive` is in the list because a book nothing could be heard on is still a book
 * waiting for an answer, and the answer may well be that someone listened and it is fine.
 * Acceptance exists for exactly that: the audit is evidence, not proof.
 */
export function isAudioAuditFlagged(
  verdict: AudioAuditVerdict | null | undefined,
  accepted: boolean | null | undefined,
): boolean {
  return !!verdict && FLAGGING.includes(verdict) && !accepted
}

/** Whether the verdict is one that can be overruled at all, accepted or not. */
export function isAudioAuditOverrulable(verdict: AudioAuditVerdict | null | undefined): boolean {
  return !!verdict && FLAGGING.includes(verdict)
}

/**
 * How each verdict reads wherever a book is listed: a badge on a cover, a cell on the
 * Tags page, the word in a tooltip.
 *
 * The Books page kept its own copy of this and of the flagging rule, and both drifted
 * from the ones here: it never learned `inconclusive`, so a book the Tags page called
 * "Unclear" carried no badge at all, and it switched on the verdict alone, so a book
 * whose owner had already answered it stayed badged and stayed in the library's
 * "Metadata Mismatch" filter while being absent from the Tags page's. A status shown in
 * two places is decided in one.
 */
export const AUDIO_AUDIT_LABELS: Record<AudioAuditVerdict, string> = {
  match: 'Matches',
  'narrator-mismatch': 'Narrator differs',
  incomplete: 'Cuts off',
  mismatch: 'Different book',
  inconclusive: 'Unclear',
}

/**
 * The badge for a book the audit is flagging, or null when there is nothing to show —
 * the audio introduces itself as this book, nobody has listened yet, or someone listened
 * and overruled the verdict.
 */
export function audioAuditIssueLabel(
  verdict: AudioAuditVerdict | null | undefined,
  accepted: boolean | null | undefined,
): string | null {
  if (!verdict || !isAudioAuditFlagged(verdict, accepted)) return null
  return AUDIO_AUDIT_LABELS[verdict]
}

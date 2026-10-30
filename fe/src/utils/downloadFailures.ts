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

import type { DownloadFailureReason } from '@/types'

/**
 * The word a failed row carries instead of "Failed".
 *
 * A list of a hundred rows all saying "Failed" says nothing about which of them is
 * worth opening: a release whose articles have expired needs another source, a wrong
 * archive password means the wrong NZB was grabbed, and a duplicate means the client
 * threw the grab away without fetching anything. Each is a different thing to do.
 *
 * Unknown deliberately has no word of its own - the client's own message is already
 * shown beside the badge, and "Unknown" would displace it with less.
 */
const FAILURE_REASON_LABELS: Record<DownloadFailureReason, string | null> = {
  Unknown: null,
  Health: 'Health',
  Damaged: 'Damaged',
  Password: 'Password',
  Duplicate: 'Duplicate',
  DiskSpace: 'No space',
  PostProcessing: 'Move failed',
  MissingFiles: 'Files missing',
}

/** The label for a classified failure, or null when there is nothing better to say. */
export function failureReasonLabel(reason?: string | null): string | null {
  if (!reason) return null
  return FAILURE_REASON_LABELS[reason as DownloadFailureReason] ?? null
}

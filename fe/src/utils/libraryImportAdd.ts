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
import { apiService } from '@/services/api'
import type { AudibleBookMetadata, AudiobookSeriesMembership, SearchResult } from '@/types'

export const ASIN_PATTERN = /^[A-Z0-9]{10}$/i

/**
 * Whether two folder paths name the same folder, for the purpose of deciding
 * whether a file can be registered in place against a held record. Deliberately
 * lenient about separators and a trailing slash; the backend applies the
 * authoritative comparison with the root folder's real case-sensitivity rules.
 */
export function sameFolder(left: string, right: string): boolean {
  const normalize = (value: string) => value.replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase()
  return normalize(left) === normalize(right)
}

export function normalizeGenres(genres: unknown): string[] | undefined {
  if (!Array.isArray(genres)) return genres as string[] | undefined
  return genres
    .map((g) => (typeof g === 'string' ? g : ((g as { name?: string })?.name ?? '')))
    .filter(Boolean)
}

export function matchToMetadata(result: SearchResult): AudibleBookMetadata {
  const authors: string[] =
    result.authors && result.authors.length > 0
      ? result.authors.map((a) => a.name ?? '').filter(Boolean)
      : []

  // series may come back as AudibleSeries[] from the search endpoint. A book can belong to
  // more than one series (Audnexus seriesPrimary/seriesSecondary), so every entry becomes a
  // membership; the scalar fields below stay populated from the primary for older consumers.
  const seriesRaw = result.series as unknown
  const seriesEntries = Array.isArray(seriesRaw)
    ? (seriesRaw as Array<{ name?: string; asin?: string; position?: string }>)
    : []
  const seriesMemberships: AudiobookSeriesMembership[] = seriesEntries
    .filter((entry) => (entry?.name ?? '').trim().length > 0)
    .map((entry, index) => {
      const asin = entry.asin?.trim()
      return {
        seriesName: (entry.name ?? '').trim(),
        seriesNumber: entry.position?.trim() || undefined,
        // The search fallback branch fills `asin` with the series *name* when the ASIN
        // re-fetch fails, so only keep a value that actually looks like an ASIN. Until now
        // that bogus value was discarded anyway; a membership would persist it.
        seriesAsin: asin && ASIN_PATTERN.test(asin) ? asin : undefined,
        isPrimary: index === 0,
        sortOrder: index,
      }
    })
  const seriesItem = seriesEntries[0] ?? null
  const series = seriesItem?.name ?? (typeof seriesRaw === 'string' ? seriesRaw : undefined)
  const seriesNumber = seriesItem?.position ?? result.seriesNumber
  const seriesAsin = seriesItem?.asin ?? result.seriesAsin

  return {
    title: result.title ?? '',
    asin: result.asin ?? '',
    authors,
    subtitle: result.subtitle,
    series,
    seriesNumber,
    seriesAsin,
    ...(seriesMemberships.length > 0 ? { seriesMemberships } : {}),
    description: result.description,
    publisher: result.publisher,
    language: result.language,
    runtime: result.runtime ?? (result.lengthMinutes ? result.lengthMinutes * 60 : undefined),
    imageUrl: result.imageUrl,
    // SearchResult.genres comes as objects {asin, name, type} from Audible;
    // AudibleBookMetadata.genres expects string[] (genre names only)
    genres: normalizeGenres(result.genres),
    narrators: result.narrators?.map((n) => n.name ?? '').filter(Boolean),
    publishYear: result.releaseDate?.substring(0, 4) ?? result.publishDate?.substring(0, 4),
    metadataSource: result.metadataSource,
  }
}

// Enrich metadata with full Audible data before adding to library.
// Search results often have authors: [{ asin, name: undefined }] — the full
// metadata fetch is the only way to get real author/narrator names.
export async function enrichMetadata(match: SearchResult): Promise<AudibleBookMetadata> {
  const base = matchToMetadata(match)
  if (!match.asin) return base
  try {
    type AudiblePayload = {
      authors?: { name?: string }[]
      narrators?: { name?: string }[]
    }
    const resp = await apiService.getAudibleMetadata<
      { source?: string; metadata?: AudiblePayload } | AudiblePayload
    >(match.asin)
    const raw: AudiblePayload =
      resp && 'metadata' in resp && resp.metadata ? resp.metadata : (resp as AudiblePayload)
    const enrichedAuthors = (raw.authors ?? []).map((a) => a?.name ?? '').filter(Boolean)
    const enrichedNarrators = (raw.narrators ?? []).map((n) => n?.name ?? '').filter(Boolean)
    return {
      ...base,
      ...(enrichedAuthors.length > 0 ? { authors: enrichedAuthors } : {}),
      ...(enrichedNarrators.length > 0 ? { narrators: enrichedNarrators } : {}),
    }
  } catch {
    return base
  }
}

export type LibraryImportAction = 'none' | 'move' | 'hardlink/copy'

export interface AddAndImportRequest {
  /** The directory the manual import is rooted at; every source file sits under it. */
  folderPath: string
  sourceFiles: string[]
  /** A catalogue match, or null to add from the file's own metadata. */
  match: SearchResult | null
  fileMetadata: AudibleBookMetadata | null
  /** The library root the files are placed into (ignored for action 'none'). */
  rootFolderPath: string
  action: LibraryImportAction
  monitored: boolean
  separateBook: boolean
}

export interface AddAndImportResult {
  audiobookId: number
  warnings: string[]
}

/**
 * Add a book to the library and import its files into it: the two calls every
 * file-first import makes, with the "already in the library" answer handled the
 * same way for each caller.
 *
 * Shared by Library Import and the Found tab so the two cannot drift.
 */
export async function addAndImportBook(request: AddAndImportRequest): Promise<AddAndImportResult> {
  const { match } = request
  let audiobookId: number
  try {
    // A file-metadata import has no catalogue match to enrich or send, and is
    // never monitored: the book is already on disk, and without an ASIN an
    // automatic search cannot identify a release for it.
    const metadata = match ? await enrichMetadata(match) : request.fileMetadata!
    const sanitizedMatch = match
      ? {
          ...match,
          genres: normalizeGenres(match.genres),
          series: Array.isArray(match.series)
            ? ((match.series as Array<{ name?: string }>)[0]?.name ?? undefined)
            : match.series,
        }
      : undefined
    const { audiobook } = await apiService.addToLibrary(metadata, {
      monitored: request.monitored,
      destinationPath: request.action === 'none' ? request.folderPath : request.rootFolderPath,
      searchResult: sanitizedMatch,
      allowDuplicateEdition: request.separateBook,
    })
    audiobookId = audiobook.id
  } catch (e: unknown) {
    // 409 = book already in library, extract existing audiobook from response body
    const err = e as { status?: number; body?: unknown }
    if (err?.status === 409 && err?.body) {
      const body = typeof err.body === 'string' ? JSON.parse(err.body) : err.body
      if (body?.audiobook?.id) {
        audiobookId = body.audiobook.id
        // Attaching to the held record registers the file into that record's
        // folder. When the file is somewhere else entirely, in-place
        // registration refuses it, and the backend's reason never reaches the
        // UI - so say plainly what happened and what the two ways out are.
        const heldBasePath: string | undefined = body.audiobook.basePath
        if (
          request.action === 'none' &&
          heldBasePath &&
          !sameFolder(heldBasePath, request.folderPath)
        ) {
          throw new Error(
            `"${body.audiobook.title ?? 'A book'}" is already in the library at ` +
              `${heldBasePath}, which is not the folder this file is in. ` +
              `Tick "Separate book" on this row to add it as its own record, ` +
              `or choose Move/Copy so the file is placed into that folder.`,
          )
        }
        // Mutation imports may compatibility-route an existing audiobook to the
        // selected destination. In-place registration must never rewrite BasePath:
        // the existing file has to belong to the audiobook's current managed folder.
        if (request.action !== 'none' && request.rootFolderPath) {
          try {
            await apiService.updateAudiobook(audiobookId, { basePath: request.rootFolderPath })
          } catch {
            // Non-critical — import continues, file may go to OutputPath fallback
          }
        }
      } else {
        throw e
      }
    } else {
      throw e
    }
  }

  const importResult = await apiService.startManualImport({
    path: request.folderPath,
    mode: 'interactive',
    action: request.action,
    includeCompanionFiles: request.action !== 'none',
    cleanupEmptySourceFolders: request.action === 'move',
    items: request.sourceFiles.map((fullPath) => ({
      fullPath,
      matchedAudiobookId: audiobookId,
    })),
  })
  const failedResult = importResult.results?.find((result) => !result.success)
  if (failedResult || importResult.importedCount !== request.sourceFiles.length) {
    const reason = failedResult?.error ?? failedResult?.skipReason
    throw new Error(
      reason ??
        `Only ${importResult.importedCount} of ${request.sourceFiles.length} file(s) were imported`,
    )
  }

  const warnings: string[] = []
  for (const result of importResult.results ?? []) {
    if (result.success && result.warning && !warnings.includes(result.warning)) {
      warnings.push(result.warning)
    }
  }

  return { audiobookId, warnings }
}

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
import { ref } from 'vue'

/**
 * How much of a book a preview player will play.
 *
 * The endpoints serve whole files so the browser can seek — an M4B keeps its moov atom
 * at the end and cannot start without reading it — so this limit lives in the player and
 * is a convention of the UI rather than a boundary the API enforces. It is enough to
 * answer "is this the book I think it is?" without streaming a whole audiobook across
 * the network.
 */
export const PREVIEW_SECONDS = 120

/**
 * The player currently sounding, anywhere in the app.
 *
 * Module state rather than per-instance state, because the whole point is that every
 * player reads the same value: starting one stops the one already playing. Two books
 * talking over each other tells you nothing about either.
 */
export const activePreviewId = ref<string | null>(null)

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
  <div class="audiobook-detail" v-if="!loading && audiobook">
    <!-- Top Navigation Bar -->
    <div class="top-nav">
      <button class="nav-btn" @click="goBack">
        <PhArrowLeft />
        Back
      </button>
      <div class="nav-actions">
        <div class="primary-actions">
          <button
            v-for="action in primaryTopActions"
            :key="`primary-${action.key}`"
            :class="['nav-btn', 'icon-button', action.desktopClass]"
            :disabled="action.disabled"
            @click="runTopAction(action)"
            :title="action.title"
            :aria-label="action.ariaLabel"
            :aria-pressed="action.key === 'monitor' ? audiobook.monitored : undefined"
          >
            <component
              :is="action.icon"
              v-bind="action.iconProps || {}"
              :class="action.iconClass"
            />
          </button>
        </div>

        <!-- Desktop: show all actions inline -->
        <div class="secondary-actions tabs-desktop">
          <button
            v-for="action in secondaryTopActions"
            :key="`secondary-${action.key}`"
            :class="['nav-btn', 'icon-button', action.desktopClass]"
            :disabled="action.disabled"
            @click="runTopAction(action)"
            :title="action.title"
            :aria-label="action.ariaLabel"
          >
            <component
              :is="action.icon"
              v-bind="action.iconProps || {}"
              :class="action.iconClass"
            />
          </button>
        </div>

        <!-- Mobile: collapse remaining actions into a More dropdown -->
        <div class="more-wrapper tabs-mobile">
          <button
            class="nav-btn more-btn"
            @click.stop="showMoreActions = !showMoreActions"
            :aria-expanded="showMoreActions"
            title="More actions"
          >
            <PhCaretDown />
            More
          </button>
          <div v-if="showMoreActions" class="more-dropdown" @click.stop>
            <button
              v-for="action in topActions"
              :key="`more-${action.key}`"
              :class="['dropdown-item', action.mobileClass]"
              :disabled="action.disabled"
              @click="runTopAction(action, true)"
            >
              <component
                :is="action.icon"
                v-bind="action.iconProps || {}"
                :class="action.iconClass"
              />
              <span>{{ action.label }}</span>
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Hero Section -->
    <div class="hero-section">
      <div class="backdrop" :style="{ backgroundImage: `url(${coverImageUrl})` }"></div>
      <div class="hero-content">
        <div class="poster-container">
          <img
            :src="coverImageUrl"
            :alt="audiobook.title"
            class="poster"
            loading="lazy"
            decoding="async"
            @error="handleImageError"
          />
        </div>
        <div class="info-section">
          <div class="hero-title-row">
            <h1 class="title">{{ safeText(audiobook.title) }}</h1>
            <div class="hero-actions">
              <button
                class="hero-refresh-btn"
                :disabled="rescanningMetadata"
                @click="rescanMetadata"
                title="Re-fetch this book's metadata from its provider"
              >
                <component
                  :is="rescanningMetadata ? PhSpinner : PhArrowClockwise"
                  :class="rescanningMetadata ? 'ph-spin' : undefined"
                />
                {{ rescanningMetadata ? 'Refreshing...' : 'Refresh Metadata' }}
              </button>
              <button
                class="hero-refresh-btn"
                :disabled="rescanningMetadata"
                @click="showFixMatchModal = true"
                title="Search for the right edition and re-match this book to it"
              >
                <PhMagnifyingGlass />
                Fix Match
              </button>
            </div>
          </div>
          <div class="subtitle" v-if="showSubtitle">{{ safeText(audiobook.subtitle) }}</div>
          <div v-if="displaySeriesMemberships.length > 0" class="hero-series">
            <div
              v-for="(membership, index) in displaySeriesMemberships"
              :key="`hero-series-${membership.seriesName}-${membership.seriesNumber || index}`"
              class="detail-series-membership"
            >
              <button
                type="button"
                class="tag-badge detail-link-tag"
                @click="goToSeriesCollection(membership.seriesName)"
              >
                {{ safeText(membership.seriesName) }}
              </button>
              <span v-if="membership.seriesNumber" class="detail-series-number">
                #{{ membership.seriesNumber }}
              </span>
              <span
                v-if="membership.isPrimary && displaySeriesMemberships.length > 1"
                class="identifier-badge primary"
              >
                Primary
              </span>
            </div>
          </div>

          <div class="meta-info">
            <span class="authors" v-if="audiobook.authors?.length">
              <PhUser />
              <span class="meta-author-list">
                <template v-for="(author, index) in audiobook.authors" :key="author">
                  <span v-if="index > 0" class="meta-author-sep">,&nbsp;</span>
                  <button
                    type="button"
                    class="meta-author-link"
                    :title="`Open ${author}`"
                    @click="goToAuthorCollection(author)"
                  >
                    {{ safeText(author) }}
                  </button>
                </template>
              </span>
            </span>
            <span class="runtime" v-if="audiobook.runtime">
              <PhClock />
              {{ formatRuntime(audiobook.runtime) }}
            </span>
            <span class="formats" v-if="fileFormats.length > 0">
              <PhFileAudio />
              {{ fileFormats.join(', ') }}
            </span>
            <span class="rating" v-if="listenerRating" :title="ratingTooltip">
              <PhStar />
              {{ listenerRating.overall }}
              <span class="rating-count" v-if="listenerRating.count">
                ({{ listenerRating.count.toLocaleString() }})
              </span>
              <span class="rating-split" v-if="ratingBreakdown.length">
                <template v-for="(part, index) in ratingBreakdown" :key="part.label">
                  <span v-if="index > 0" class="rating-split-sep">&nbsp;&middot;&nbsp;</span>
                  {{ part.label }} {{ part.value }}
                </template>
              </span>
            </span>
          </div>

          <!-- One row of facts about this book, led by the control that changes one -->
          <div class="key-details">
            <Pill
              class="hero-monitor-pill"
              interactive
              :variant="audiobook.monitored ? 'primary' : 'default'"
              :title="audiobook.monitored ? 'Stop monitoring this book' : 'Monitor this book'"
              @click="toggleMonitored"
            >
              <PhBookmark :weight="audiobook.monitored ? 'fill' : 'regular'" />
              {{ audiobook.monitored ? 'Monitored' : 'Not Monitored' }}
            </Pill>
            <AudioPreviewPlayer
              class="hero-preview"
              :preview-id="`book-${audiobook.id}`"
              :src="firstPlayableFile ? filePreviewSrc(firstPlayableFile.id) : ''"
              label="Preview"
              disabled-title="This book has no files to play yet"
            />
            <div class="detail-item" v-if="displayBasePath">
              <PhFolder />
              <span class="file-path">{{ displayBasePath }}</span>
            </div>
            <div class="detail-item" v-if="audiobook.fileSize">
              <PhDatabase />
              <span>{{ formatFileSize(audiobook.fileSize) }}</span>
            </div>
            <div class="detail-item" v-if="audiobook.quality">
              <PhSpeakerHigh />
              <span>{{ audiobook.quality }}</span>
            </div>
            <div class="detail-item" v-if="audiobook.language">
              <PhGlobe />
              <span>{{ capitalizeFirst(audiobook.language) }}</span>
            </div>
            <!-- Abridged is the exception worth flagging; every other book being
                 "Unabridged" says nothing. Matches how the modals badge it. -->
            <div class="detail-item" v-if="audiobook.abridged">
              <PhTag />
              <span>Abridged</span>
            </div>
            <Pill variant="success" v-if="assignedProfileName">
              <PhStar />
              Quality: {{ assignedProfileName }}
            </Pill>
            <Pill variant="default" v-if="audiobook.version">
              <PhMusicNotes />
              {{ audiobook.version }}
            </Pill>
            <Pill variant="default" v-if="audiobook.edition">
              <PhTag />
              {{ audiobook.edition }}
            </Pill>
          </div>

          <div class="description" v-if="descriptionText">
            <div class="description-content">
              {{ displayedDescription
              }}<button
                v-if="canToggleDescription"
                class="show-more-btn"
                @click="showFullDescription = !showFullDescription"
              >
                {{ showFullDescription ? 'Show Less' : 'Show More' }}
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Tabs Section -->
    <div class="tabs-container">
      <!-- Mobile dropdown -->
      <div class="tabs-mobile">
        <CustomSelect v-model="activeTab" :options="mobileTabOptions" class="tab-dropdown" />
      </div>

      <!-- Desktop tabs -->
      <div class="tabs-desktop">
        <div class="tabs">
          <button
            class="tab"
            :class="{ active: activeTab === 'details' }"
            @click="activeTab = 'details'"
          >
            <PhInfo />
            Details
          </button>
          <button
            class="tab"
            :class="{ active: activeTab === 'files' }"
            @click="activeTab = 'files'"
          >
            <PhFile />
            Files
          </button>
          <button
            class="tab"
            :class="{ active: activeTab === 'chapters' }"
            @click="activeTab = 'chapters'"
          >
            <PhListNumbers />
            Chapters
            <span
              v-if="chapterTabBadge"
              class="tab-badge"
              :class="`tab-badge--${chapterTabBadge.severity}`"
              :title="chapterTabBadge.title"
              >!</span
            >
          </button>
          <button
            class="tab"
            :class="{ active: activeTab === 'transcript' }"
            @click="activeTab = 'transcript'"
          >
            <PhEar />
            Transcript
            <span v-if="audioTabBadge" class="tab-badge tab-badge--issue" :title="audioTabBadge"
              >!</span
            >
          </button>
          <button class="tab" :class="{ active: activeTab === 'tags' }" @click="activeTab = 'tags'">
            <PhTag />
            Tags
          </button>
          <button
            class="tab"
            :class="{ active: activeTab === 'history' }"
            @click="activeTab = 'history'"
          >
            <PhClockCounterClockwise />
            History
          </button>
        </div>
      </div>
    </div>

    <!-- Tab Content -->
    <div class="tab-content">
      <!-- Details Tab -->
      <div id="details" v-if="activeTab === 'details'" class="details-content">
        <div class="details-grid">
          <div class="detail-card">
            <h3>Author Information</h3>
            <div class="detail-row" v-if="audiobook.authors?.length">
              <span class="label">Author(s):</span>
              <div class="value detail-link-tags">
                <button
                  v-for="author in audiobook.authors"
                  :key="author"
                  type="button"
                  class="tag-badge detail-link-tag"
                  @click="goToAuthorCollection(author)"
                >
                  {{ safeText(author) }}
                </button>
              </div>
            </div>
            <div class="detail-row" v-if="audiobook.narrators?.length">
              <span class="label">Narrator(s):</span>
              <div class="value detail-link-tags">
                <button
                  v-for="narrator in audiobook.narrators"
                  :key="narrator"
                  type="button"
                  class="tag-badge detail-link-tag"
                  @click="goToNarratorCollection(narrator)"
                >
                  {{ safeText(narrator) }}
                </button>
              </div>
            </div>
          </div>

          <div class="detail-card">
            <h3>Publication Details</h3>
            <div class="detail-row" v-if="audiobook.publisher">
              <span class="label">Publisher:</span>
              <div class="value detail-link-tags">
                <button
                  type="button"
                  class="tag-badge detail-link-tag"
                  @click="goToPublisherCollection(audiobook.publisher)"
                >
                  {{ safeText(audiobook.publisher) }}
                </button>
              </div>
            </div>
            <div class="detail-row" v-if="audiobook.publishedDate || audiobook.publishYear">
              <span class="label">Release Date:</span>
              <span class="value">{{
                audiobook.publishedDate
                  ? formatDate(audiobook.publishedDate)
                  : audiobook.publishYear
              }}</span>
            </div>
            <div class="detail-row" v-if="audiobook.language">
              <span class="label">Language:</span>
              <span class="value">{{ capitalizeFirst(audiobook.language) }}</span>
            </div>
            <div class="detail-row" v-if="audiobook.edition">
              <span class="label">Edition:</span>
              <span class="value">{{ safeText(audiobook.edition) }}</span>
            </div>
          </div>

          <div class="detail-card" v-if="displaySeriesMemberships.length">
            <h3>Series Information</h3>
            <div class="detail-row">
              <span class="label">Series:</span>
              <div class="value detail-link-tags detail-series-memberships">
                <div
                  v-for="(membership, index) in displaySeriesMemberships"
                  :key="`${membership.seriesName}-${membership.seriesNumber || index}`"
                  class="detail-series-membership"
                >
                  <button
                    type="button"
                    class="tag-badge detail-link-tag"
                    @click="goToSeriesCollection(membership.seriesName)"
                  >
                    {{ safeText(membership.seriesName) }}
                  </button>
                  <span v-if="membership.seriesNumber" class="detail-series-number">
                    #{{ membership.seriesNumber }}
                  </span>
                  <span v-if="membership.isPrimary" class="identifier-badge primary">
                    Primary
                  </span>
                </div>
              </div>
            </div>
          </div>

          <div class="detail-card">
            <h3>Identifiers</h3>
            <div class="detail-row" v-if="audibleSourceUrl">
              <span class="label">Metadata Source:</span>
              <span class="value">
                <a :href="audibleSourceUrl" target="_blank" rel="noopener noreferrer">Audible</a>
              </span>
            </div>
            <div class="detail-row detail-row-stacked" v-if="displayIdentifiers.length">
              <span class="label">Associated IDs:</span>
              <div class="value identifiers-list">
                <div
                  v-for="identifier in displayIdentifiers"
                  :key="identifier.key"
                  class="identifier-item"
                >
                  <span class="identifier-type">{{ identifier.typeLabel }}</span>
                  <a
                    v-if="identifier.href"
                    :href="identifier.href"
                    target="_blank"
                    rel="noopener noreferrer"
                    class="identifier-link"
                  >
                    {{ identifier.value }}
                  </a>
                  <span v-else class="identifier-link">{{ identifier.value }}</span>
                  <span v-if="identifier.isPrimary" class="identifier-badge primary">Primary</span>
                </div>
              </div>
            </div>
            <div
              class="detail-row"
              v-else-if="audiobook.asin || audiobook.isbn || audiobook.openLibraryId"
            >
              <span class="label">Associated IDs:</span>
              <span class="value">Unavailable</span>
            </div>
          </div>

          <div class="detail-card" v-if="audiobook.genres && audiobook.genres.length">
            <h3>Genres</h3>
            <div class="genre-tags">
              <button
                v-for="genre in audiobook.genres"
                :key="genre"
                type="button"
                class="genre-tag detail-link-tag detail-genre-tag"
                @click="goToGenreCollection(genre)"
              >
                {{ genre }}
              </button>
            </div>
          </div>

          <div class="detail-card" v-if="audiobook.tags && audiobook.tags.length">
            <h3>Tags</h3>
            <div class="tags-list">
              <span v-for="tag in audiobook.tags" :key="tag" class="tag-badge">
                {{ tag }}
              </span>
            </div>
          </div>
        </div>
      </div>

      <!-- Files Tab -->
      <div id="files" v-if="activeTab === 'files'" class="files-content">
        <div class="files-header">
          <h3>Files</h3>
          <div class="files-actions">
            <!--
              A scan only flags a file it cannot find; removing the row is the operator's
              call, made here, once they know the file is gone rather than the share late.
            -->
            <button
              v-if="notFoundFiles.length > 0"
              type="button"
              class="file-repair-btn file-repair-btn--danger"
              :disabled="removingNotFound"
              :title="`Remove the ${notFoundFiles.length} file record(s) a scan could not find. Nothing on disk is touched.`"
              @click="removeNotFoundFiles"
            >
              <PhFileX />
              {{
                removingNotFound
                  ? 'Removing…'
                  : `Remove ${notFoundFiles.length} not-found file${notFoundFiles.length === 1 ? '' : 's'}`
              }}
            </button>
            <div v-if="displayedScanJobId" class="scan-job-status">
              <div class="job-row">
                <PhClock />
                <strong>Scan job:</strong>
                <span class="job-id">{{ displayedScanJobId }}</span>
              </div>
              <div class="job-status">
                <span :class="['status', scanQueued ? 'queued' : 'completed']">
                  {{ displayedScanStatus }}
                </span>
              </div>
            </div>
          </div>
        </div>
        <div v-if="audiobook.files && audiobook.files.length" class="file-list">
          <div
            v-for="f in audiobook.files"
            :key="f.id"
            class="file-item"
            :class="{
              expanded: isFileAccordionExpanded(f.id),
              'file-item--not-found': !!f.notFoundSince,
            }"
          >
            <div class="file-header" @click="toggleFileAccordion(f.id)">
              <div class="file-info">
                <!-- The player owns its own clicks; the row around it opens the accordion. -->
                <AudioPreviewPlayer
                  class="file-preview"
                  :preview-id="`file-${f.id}`"
                  :src="filePreviewSrc(f.id)"
                  @click.stop
                />
                <PhFileAudio />
                <span class="file-name">{{ getFileName(f.path) }}</span>
                <small class="file-meta"
                  >• {{ f.format ? f.format.toUpperCase() : '' }}
                  {{ f.durationSeconds ? '• ' + formatDuration(f.durationSeconds) : '' }}</small
                >
              </div>
              <div class="file-actions">
                <span
                  v-if="f.notFoundSince"
                  class="file-chapter-badge"
                  :title="`Not at its path since ${formatNotFoundSince(f.notFoundSince)}. A scan that finds it again clears this.`"
                >
                  <PhFileX />
                  Not found
                </span>
                <span
                  v-if="chapterBadge(f.chapterHealth)"
                  class="file-chapter-badge"
                  :class="{
                    'file-chapter-badge--note':
                      f.chapterHealth === 'generic-titles' || f.chapterHealth === 'none',
                  }"
                  :title="f.chapterReason ?? undefined"
                >
                  <PhListNumbers />
                  {{ chapterBadge(f.chapterHealth) }}
                </span>
                <button
                  v-if="chapterBadge(f.chapterHealth)"
                  type="button"
                  class="file-repair-btn"
                  :disabled="tagWriteInFlight"
                  :title="
                    tagWriteInFlight ? writeTagsTitle : 'Preview and rebuild this file’s chapters'
                  "
                  @click.stop="openChapterRepair(f.id)"
                >
                  Repair
                </button>
                <span class="file-size" v-if="f.size">{{ formatFileSize(f.size) }}</span>
                <span class="file-size" v-else>Unknown size</span>
                <PhCaretDown
                  class="accordion-toggle"
                  :class="{ rotated: isFileAccordionExpanded(f.id) }"
                />
              </div>
            </div>
            <div v-if="isFileAccordionExpanded(f.id)" class="file-accordion">
              <table class="metadata-table">
                <tbody>
                  <tr v-if="f.path">
                    <td class="metadata-label">Path:</td>
                    <td class="metadata-value">{{ getFullPath(f.path) }}</td>
                  </tr>
                  <tr v-if="f.size !== undefined">
                    <td class="metadata-label">Size:</td>
                    <td class="metadata-value">{{ formatFileSize(f.size) }}</td>
                  </tr>
                  <tr v-if="f.durationSeconds !== undefined">
                    <td class="metadata-label">Duration:</td>
                    <td class="metadata-value">{{ formatDuration(f.durationSeconds) }}</td>
                  </tr>
                  <tr v-if="f.format">
                    <td class="metadata-label">Format:</td>
                    <td class="metadata-value">{{ f.format.toUpperCase() }}</td>
                  </tr>
                  <tr v-if="f.container">
                    <td class="metadata-label">Container:</td>
                    <td class="metadata-value">{{ f.container }}</td>
                  </tr>
                  <tr v-if="f.codec">
                    <td class="metadata-label">Codec:</td>
                    <td class="metadata-value">{{ f.codec }}</td>
                  </tr>
                  <tr v-if="f.bitrate !== undefined">
                    <td class="metadata-label">Bitrate:</td>
                    <td class="metadata-value">{{ f.bitrate }} kbps</td>
                  </tr>
                  <tr v-if="f.sampleRate !== undefined">
                    <td class="metadata-label">Sample Rate:</td>
                    <td class="metadata-value">{{ f.sampleRate }} Hz</td>
                  </tr>
                  <tr v-if="f.channels !== undefined">
                    <td class="metadata-label">Channels:</td>
                    <td class="metadata-value">{{ f.channels }}</td>
                  </tr>
                  <tr v-if="f.createdAt">
                    <td class="metadata-label">Created:</td>
                    <td class="metadata-value">{{ formatDate(f.createdAt) }}</td>
                  </tr>
                  <tr v-if="f.source">
                    <td class="metadata-label">Source:</td>
                    <td class="metadata-value">{{ f.source }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
        <div v-else class="empty-files">
          <PhFileDashed />
          <p>No files available</p>
          <p class="hint">This audiobook hasn't been downloaded yet</p>
        </div>
      </div>

      <!-- Chapters Tab -->
      <div id="chapters" v-if="activeTab === 'chapters'" class="chapters-content">
        <!--
          The chapter verdict, always shown, so "checked and fine" and "never checked"
          do not look the same. Checking reads the files' atoms; it writes nothing.
        -->
        <div
          v-if="audiobook && audiobook.files && audiobook.files.length"
          class="audio-audit audio-audit--summary"
          :class="chapterPanelClass"
        >
          <PhListNumbers class="audio-audit-icon" />
          <div class="audio-audit-body">
            <div class="audio-audit-verdict">{{ chapterSummary.headline }}</div>
            <div class="audio-audit-reason">{{ chapterSummary.detail }}</div>
          </div>
          <div class="audio-audit-actions">
            <button
              v-if="chapterSummary.repairableFileIds.length > 0"
              type="button"
              class="file-repair-btn"
              :disabled="tagWriteInFlight"
              title="Preview and rebuild the chapters of the files that need it"
              @click="openChapterRepair(...chapterSummary.repairableFileIds)"
            >
              Repair chapters
            </button>
            <button
              type="button"
              class="file-repair-btn file-repair-btn--quiet"
              :disabled="checkingChapters"
              :title="
                checkingChapters
                  ? 'Reading the files…'
                  : 'Read each file’s chapter atom and chapter track and judge them'
              "
              @click="checkChapters"
            >
              {{
                checkingChapters
                  ? 'Checking…'
                  : chapterSummary.checked
                    ? 'Check again'
                    : 'Check chapters'
              }}
            </button>
          </div>
        </div>

        <ChapterListPanel
          v-if="audiobook"
          ref="chapterPanel"
          :audiobookId="audiobook.id"
          :disabled="tagWriteInFlight"
          :disabledReason="writeTagsTitle"
          @repair="(fileId) => openChapterRepair(fileId)"
          @replan="onReplan"
        />
      </div>

      <!-- Transcript Tab: what the narrator says the book is, beside what the record says. -->
      <div id="transcript" v-if="activeTab === 'transcript'" class="credits-content">
        <div v-if="!audiobook.files || !audiobook.files.length" class="credits-empty">
          <PhEar />
          <p>This book has no audio files here to listen to.</p>
        </div>

        <template v-else>
          <div
            class="audio-audit"
            :class="
              audiobook.audioAuditAccepted
                ? 'audio-audit--accepted'
                : audiobook.audioAudit
                  ? `audio-audit--${audiobook.audioAudit}`
                  : 'audio-audit--none'
            "
          >
            <PhEar class="audio-audit-icon" />
            <div class="audio-audit-body">
              <div class="audio-audit-verdict">
                {{ audiobook.audioAudit ? audioAuditLabel : 'Not yet listened to.' }}
              </div>
              <div class="audio-audit-reason">
                {{
                  audiobook.audioAudit
                    ? audiobook.audioAuditReason
                    : 'Transcribing hears the first minute and the last of the book — where the title, author, narrator and publisher are read — and checks what it hears against this record.'
                }}
              </div>
              <div v-if="audiobook.audioAuditedAt" class="audio-audit-when">
                Heard {{ formatAuditedAt(audiobook.audioAuditedAt) }}
                <template v-if="audiobook.audioAuditAccepted">
                  · kept as recorded by you, so it is not counted as a problem
                </template>
              </div>
            </div>
            <div class="audio-audit-actions">
              <button
                type="button"
                class="file-repair-btn"
                :disabled="audioAuditInFlight || transcriptionOff"
                :title="
                  audioAuditInFlight
                    ? 'Transcribing…'
                    : transcriptionOff
                      ? 'Transcription is off. Turn it on in Settings → Metadata Tags.'
                      : 'Hear the opening and closing and check them against the record'
                "
                @click="auditAudio"
              >
                {{
                  audioAuditInFlight
                    ? 'Transcribing…'
                    : audiobook.audioAudit
                      ? 'Transcribe again'
                      : 'Transcribe'
                }}
              </button>
            </div>
          </div>

          <template v-if="audiobook.audioAudit">
            <div class="credits-compare">
              <div class="credits-col">
                <h4>Heard in the audio</h4>
                <dl>
                  <dt>Title</dt>
                  <dd :class="{ 'credits-missing': !audiobook.audioAuditHeardTitle }">
                    {{ audiobook.audioAuditHeardTitle || 'not heard' }}
                  </dd>
                  <dt>Author</dt>
                  <dd :class="{ 'credits-missing': !audiobook.audioAuditHeardAuthor }">
                    {{ audiobook.audioAuditHeardAuthor || 'not heard' }}
                  </dd>
                  <dt>Narrator</dt>
                  <dd :class="{ 'credits-missing': !audiobook.audioAuditHeardNarrator }">
                    {{ audiobook.audioAuditHeardNarrator || 'not heard' }}
                  </dd>
                </dl>
              </div>
              <div class="credits-col">
                <h4>On record</h4>
                <dl>
                  <dt>Title</dt>
                  <dd>{{ audiobook.title }}</dd>
                  <dt>Author</dt>
                  <dd>{{ (audiobook.authors || []).join(', ') || '—' }}</dd>
                  <dt>Narrator</dt>
                  <dd>{{ (audiobook.narrators || []).join(', ') || '—' }}</dd>
                </dl>
              </div>
            </div>

            <div v-if="creditRecommendations.length" class="credits-recommend">
              <h4>What to do</h4>
              <ul>
                <li v-for="(rec, index) in creditRecommendations" :key="index">
                  <span>{{ rec.text }}</span>
                  <button
                    v-if="rec.action"
                    type="button"
                    class="file-repair-btn"
                    :disabled="rec.busy"
                    @click="rec.action"
                  >
                    {{ rec.label }}
                  </button>
                </li>
              </ul>
            </div>

            <div class="edition-check">
              <h4>Which edition is this?</h4>
              <p class="edition-explain">
                Two recordings of one book differ in length far more than a recording
                differs from its own stated runtime, so the catalogue's runtime per
                edition can say which one is on disk.
              </p>
              <button
                type="button"
                class="file-repair-btn"
                :disabled="checkingEdition"
                @click="runEditionCheck"
              >
                {{ checkingEdition ? 'Asking the catalogue…' : 'Check the edition' }}
              </button>
              <template v-if="editionCheck">
                <p class="credits-text">{{ editionCheck.reason }}</p>
                <button
                  v-if="editionCheck.outcome === 'other-edition' && editionCheck.best"
                  type="button"
                  class="file-repair-btn"
                  :disabled="applyingEdition"
                  @click="adoptEdition"
                >
                  {{ applyingEdition ? 'Re-matching…' : `Re-match to the ${editionNarrators} edition` }}
                </button>
              </template>
            </div>

            <div class="credits-transcript">
              <h4>Opening</h4>
              <p v-if="heardOpening" class="credits-text">{{ heardOpening }}</p>
              <p v-else class="credits-missing">Nothing was heard in the first minute.</p>
              <template v-if="heardClosing">
                <h4>Closing</h4>
                <p class="credits-text">{{ heardClosing }}</p>
              </template>
            </div>
          </template>
        </template>
      </div>

      <!-- Tags Tab -->
      <div id="tags" v-if="activeTab === 'tags'" class="tags-content">
        <AudiobookTagsPanel
          v-if="audiobook"
          ref="tagsPanel"
          :audiobookId="audiobook.id"
          :disabled="tagWriteInFlight"
          :disabledReason="writeTagsTitle"
          @write="writeTags"
        />
      </div>

      <!-- History Tab -->
      <div id="history" v-if="activeTab === 'history'" class="history-content">
        <div class="history-header">
          <h3>History</h3>
          <button
            v-if="historyEntries.length > 0"
            class="refresh-btn"
            @click="loadHistory"
            :disabled="historyLoading"
          >
            <PhArrowClockwise :class="{ 'ph-spin': historyLoading }" />
            Refresh
          </button>
        </div>

        <!-- Loading State -->
        <div v-if="historyLoading" class="history-loading">
          <PhSpinner class="ph-spin" />
          <p>Loading history...</p>
        </div>

        <!-- Error State -->
        <div v-else-if="historyError" class="history-error">
          <PhWarningCircle />
          <p>{{ historyError }}</p>
          <button class="retry-btn" @click="loadHistory">Retry</button>
        </div>

        <!-- History List -->
        <div v-else-if="historyEntries.length > 0" class="history-list">
          <div v-for="entry in historyEntries" :key="entry.id" class="history-entry">
            <div class="history-icon" :class="getEventTypeClass(entry.eventType)">
              <component :is="getEventIconComponent(entry.eventType)" />
            </div>
            <div class="history-details">
              <div class="history-event">
                <span class="event-type">{{ formatEventTitle(entry.eventType) }}</span>
                <span v-if="entry.notificationSent" class="discord-pill">
                  <PhDiscordLogo :size="14" />
                  Notified
                </span>
              </div>
              <div v-if="entry.message" class="history-message">{{ entry.message }}</div>
              <div class="history-time">{{ formatHistoryTime(entry.timestamp) }}</div>
            </div>
          </div>
        </div>

        <!-- Empty State -->
        <div v-else class="empty-history">
          <PhClockCounterClockwise />
          <p>No history available</p>
          <p class="hint">Activity for this audiobook will appear here</p>
        </div>
      </div>
    </div>

    <DeleteConfirmationModal
      :visible="showDeleteDialog"
      title="Delete Audiobook"
      :confirmText="deleting ? 'Deleting...' : 'Delete'"
      @close="cancelDelete"
      @confirm="executeDelete"
    >
      <template #default>
        <p>
          Are you sure you want to delete <strong>{{ audiobook.title }}</strong
          >?
        </p>
        <p class="warning-text">
          This action cannot be undone. The audiobook data and cached images will be permanently
          removed.
        </p>
        <div class="delete-options">
          <div class="checkbox-row">
            <label class="checkbox-wrapper checkbox-label">
              <input
                v-model="deleteFilesOnDisk"
                type="checkbox"
                class="checkbox-input"
                aria-label="Remove all files in the audiobook folder from disk"
                :disabled="!filesystemReadinessStore.filesystemReady"
              />
              <div class="checkbox-content">
                <span class="checkbox-title"
                  >Remove all files in the audiobook folder from disk</span
                >
                <small
                  >Deletes every file inside the audiobook folder when it can be identified safely.
                  Leave the folder itself unless you also choose the option below.</small
                >
              </div>
            </label>
          </div>

          <div class="checkbox-row">
            <label class="checkbox-wrapper checkbox-label">
              <input
                v-model="deleteFolderOnDisk"
                type="checkbox"
                class="checkbox-input"
                aria-label="Remove audiobook folder from disk"
                :disabled="!filesystemReadinessStore.filesystemReady"
              />
              <div class="checkbox-content">
                <span class="checkbox-title">Also remove the audiobook folder</span>
                <small
                  >Deletes the audiobook folder itself when it is safe to do so. This also removes
                  everything inside it.</small
                >
              </div>
            </label>
          </div>
        </div>
      </template>
    </DeleteConfirmationModal>
  </div>

  <!-- Loading State -->
  <div v-else-if="loading" class="loading-container">
    <PhSpinner class="ph-spin" />
    <p>Loading audiobook details...</p>
  </div>

  <!-- Error State -->
  <div v-else-if="error" class="error-container">
    <PhWarningCircle />
    <h2>Error Loading Audiobook</h2>
    <p>{{ error }}</p>
    <button @click="goBack" class="back-btn">
      <PhArrowLeft />
      Back to Library
    </button>
  </div>

  <!-- Re-match to a different edition: search, pick, rescan -->
  <LibraryImportSearchModal
    v-if="showFixMatchModal && audiobook"
    :heading="safeText(audiobook.title)"
    :initial-query="fixMatchQuery ?? audiobook.title ?? ''"
    :initial-author="fixMatchAuthor ?? (audiobook.authors || [])[0] ?? ''"
    @close="closeFixMatch"
    @select="applyMatch"
  />

  <!-- Edit Audiobook Modal -->
  <EditAudiobookModal
    :is-open="showEditModal"
    :audiobook="audiobook"
    @close="closeEditModal"
    @saved="handleEditSaved"
  />

  <!-- Manual Search Modal -->
  <ManualSearchModal
    :is-open="showManualSearchModal"
    :audiobook="audiobook"
    @close="closeManualSearch"
    @downloaded="handleDownloaded"
  />

  <RenamePreviewModal
    :visible="showOrganizeModal"
    :audiobook-ids="audiobook ? [audiobook.id] : []"
    @close="showOrganizeModal = false"
    @done="handleOrganizeDone"
  />

  <TagPreviewModal
    :visible="showTagPreviewModal"
    :audiobook-id="audiobook?.id ?? null"
    @close="showTagPreviewModal = false"
    @confirm="writeTags"
  />

  <ChapterRepairModal
    v-if="chapterRepairScopes.length > 0"
    :visible="showChapterRepairModal"
    :scopes="chapterRepairScopes"
    @close="showChapterRepairModal = false"
    @confirm="repairChapters"
  />
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed, type Component } from 'vue'
import { useToast } from '@/services/toastService'
import type { Audiobook as AudiobookType, ChapterHealth, EditionCheck } from '@/types'
import { useRoute, useRouter } from 'vue-router'
import { useLibraryStore } from '@/stores/library'
import { useConfigurationStore } from '@/stores/configuration'
import { useRootFoldersStore } from '@/stores/rootFolders'
import { useScanNotificationsStore } from '@/stores/scanNotifications'
import { useFilesystemReadinessStore } from '@/stores/filesystemReadiness'
import { useConversionJobsStore } from '@/stores/conversionJobs'
import { useTagJobsStore } from '@/stores/tagJobs'
import { apiService, ensureImageCached } from '@/services/api'
import { isApiImagesUrl } from '@/services/apiBase'
import { handleImageError } from '@/utils/imageFallback'
import { getPlaceholderUrl } from '@/utils/placeholder'
import { detectPathKind, joinPaths, isAbsolutePath } from '@/utils/path'
import { signalRService } from '@/services/signalr'
import type {
  Audiobook,
  AudiobookExternalIdentifier,
  AudiobookSeriesMembership,
  History,
  SearchResult,
  AudiobookExternalIdentifierInput,
} from '@/types'
import { safeText, stripHtmlAndNormalize, truncateAtWord } from '@/utils/textUtils'
import { isSeriesRestatement } from '@/utils/seriesUtils'
import { logger } from '@/utils/logger'
import { errorTracking } from '@/services/errorTracking'
import { useProtectedImages } from '@/composables/useProtectedImages'
import { preparePhysicalDeleteRetry } from '@/composables/useMutationSemanticsConfirmation'
import { buildAudibleProductUrl } from '@/utils/marketDomains'
import EditAudiobookModal from '@/components/domain/audiobook/EditAudiobookModal.vue'
import LibraryImportSearchModal from '@/components/domain/audiobook/LibraryImportSearchModal.vue'
import TagPreviewModal from '@/components/domain/tagging/TagPreviewModal.vue'
import { showConfirm } from '@/composables/useConfirm'
import ChapterRepairModal from '@/components/domain/tagging/ChapterRepairModal.vue'
import ChapterListPanel from '@/components/domain/tagging/ChapterListPanel.vue'
import type { ChapterRepairScope } from '@/components/domain/tagging/ChapterRepairModal.vue'
import AudiobookTagsPanel from '@/components/domain/tagging/AudiobookTagsPanel.vue'
import ManualSearchModal from '@/components/domain/search/ManualSearchModal.vue'
import RenamePreviewModal from '@/components/domain/organize/RenamePreviewModal.vue'
import CustomSelect from '@/components/form/CustomSelect.vue'
import AudioPreviewPlayer from '@/components/ui/AudioPreviewPlayer.vue'
import DeleteConfirmationModal from '@/components/feedback/DeleteConfirmationModal.vue'
import { Pill } from '@/components/base'
import {
  PhArrowLeft,
  PhArrowClockwise,
  PhBookmark,
  PhSpinner,
  PhMagnifyingGlass,
  PhFolderOpen,
  PhFileMagnifyingGlass,
  PhTrash,
  PhClock,
  PhUser,
  PhFolder,
  PhDatabase,
  PhSpeakerHigh,
  PhGlobe,
  PhTag,
  PhBookmarkSimple,
  PhStar,
  PhMusicNotes,
  PhInfo,
  PhFile,
  PhClockCounterClockwise,
  PhFileAudio,
  PhFileX,
  PhListNumbers,
  PhEar,
  PhCaretDown,
  PhFileDashed,
  PhWarningCircle,
  PhPlusCircle,
  PhDownload,
  PhUpload,
  PhPencil,
  PhHandGrabbing,
  PhFilePlus,
  PhFileMinus,
  PhCircle,
  PhDiscordLogo,
} from '@phosphor-icons/vue'

const route = useRoute()
const router = useRouter()
const libraryStore = useLibraryStore()
const configStore = useConfigurationStore()
const rootFoldersStore = useRootFoldersStore()
const scanNotificationsStore = useScanNotificationsStore()
const filesystemReadinessStore = useFilesystemReadinessStore()
const conversionJobsStore = useConversionJobsStore()
const tagJobsStore = useTagJobsStore()
const { getProtectedImageSrc } = useProtectedImages()

type DetailTab = 'details' | 'files' | 'chapters' | 'transcript' | 'tags' | 'history'

const audiobook = ref<Audiobook | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const activeTab = ref<DetailTab>('details')
const showDeleteDialog = ref(false)
const showManualSearchModal = ref(false)
const deleting = ref(false)
const deleteFilesOnDisk = ref(false)
const deleteFolderOnDisk = ref(false)
const showFullDescription = ref(false)

// Collapsing by character count rather than by height, so the toggle can follow the
// last word it hides instead of floating under a fade.
const DESCRIPTION_COLLAPSED_LENGTH = 420

/*
 * The containers this book is actually made of. Read off the files rather than the
 * `formats` field, which the detail endpoint leaves empty — only the library listing
 * fills it, and this page prefers the detail endpoint. Falls back to `formats` for the
 * store-backed path, and shows nothing for a book with no files yet.
 */
const fileFormats = computed(() => {
  const found = new Set<string>()

  for (const file of audiobook.value?.files || []) {
    const match = /\.([a-z0-9]{1,5})$/i.exec(file.path || '')
    if (match?.[1]) found.add(`.${match[1].toLowerCase()}`)
  }

  if (found.size === 0) {
    for (const format of audiobook.value?.formats || []) {
      const trimmed = (format || '').trim()
      if (trimmed) found.add(`.${trimmed.toLowerCase()}`)
    }
  }

  return Array.from(found).sort()
})

/**
 * A registered file, played by id.
 *
 * By id rather than by path: the root-folder preview the import page uses has to prove a
 * client-supplied path canonicalizes inside the folder before it will open it, and this
 * page never names a file — it names a row the scanner registered.
 */
function filePreviewSrc(fileId: number): string {
  return apiService.buildLibraryFileAudioUrl(fileId)
}

/**
 * The file the book opens with, for the preview button in the header.
 *
 * Ordered by filename the way the converter orders parts, so "the first two minutes"
 * means the start of the book and not whichever row the API happened to return first.
 * A single merged file is the common case here and sorts to itself.
 */
const firstPlayableFile = computed(() => {
  const files = (audiobook.value?.files || []).filter((f) => !!f.path)
  if (files.length === 0) return null

  return files.slice().sort((a, b) =>
    getFileName(a.path).localeCompare(getFileName(b.path), undefined, {
      numeric: true,
      sensitivity: 'base',
    }),
  )[0]
})

/**
 * Listener ratings, ready to render.
 *
 * Audible's averages arrive at full precision (4.8746...) because rounding is a display
 * decision, so it is made here. `count` is star ratings; `reviews` counts written reviews
 * and is a much smaller population, which is why the two are labelled rather than shown as
 * one bare number.
 *
 * Falls back to the Audnexus value, which exists only when Audnexus answered instead of
 * Audible. It carries no count and no split, so `source` says where the number came from
 * instead of implying a precision it does not have.
 */
const listenerRating = computed(() => {
  const book = audiobook.value
  if (!book) return null

  const overall = book.audibleRatingOverall ?? book.audnexusRating
  if (typeof overall !== 'number' || !Number.isFinite(overall)) return null

  const fromAudible = typeof book.audibleRatingOverall === 'number'

  return {
    overall: overall.toFixed(1),
    count: fromAudible ? book.audibleRatingOverallCount : undefined,
    reviews: fromAudible ? book.audibleReviewCount : undefined,
    performance: fromAudible ? book.audibleRatingPerformance : undefined,
    story: fromAudible ? book.audibleRatingStory : undefined,
    source: fromAudible ? 'Audible' : 'Audnexus',
  }
})

/** The narration/writing split, present only when Audible supplied it. */
const ratingBreakdown = computed(() => {
  const rating = listenerRating.value
  if (!rating) return []

  const parts: { label: string; value: string }[] = []
  if (typeof rating.performance === 'number') {
    parts.push({ label: 'Narration', value: rating.performance.toFixed(1) })
  }
  if (typeof rating.story === 'number') {
    parts.push({ label: 'Story', value: rating.story.toFixed(1) })
  }
  return parts
})

const ratingTooltip = computed(() => {
  const rating = listenerRating.value
  if (!rating) return ''

  const lines = [`${rating.overall} out of 5 (${rating.source})`]
  if (typeof rating.count === 'number') {
    lines.push(`${rating.count.toLocaleString()} ratings`)
  }
  if (typeof rating.reviews === 'number') {
    lines.push(`${rating.reviews.toLocaleString()} written reviews`)
  }
  for (const part of ratingBreakdown.value) {
    lines.push(`${part.label}: ${part.value}`)
  }
  return lines.join(' \u00b7 ')
})

const descriptionText = computed(() => stripHtmlAndNormalize(audiobook.value?.description) || '')
const canToggleDescription = computed(
  () => descriptionText.value.length > DESCRIPTION_COLLAPSED_LENGTH,
)
const displayedDescription = computed(() =>
  showFullDescription.value
    ? descriptionText.value
    : truncateAtWord(descriptionText.value, DESCRIPTION_COLLAPSED_LENGTH),
)
const scanning = ref(false)
const rescanningMetadata = ref(false)
const trackedScanJob = computed(() => {
  const currentBookId = audiobook.value?.id
  if (!currentBookId) return undefined

  return scanNotificationsStore.jobs
    .filter((job) => job.visible && job.audiobookId === currentBookId)
    .sort((left, right) => right.timestamp.localeCompare(left.timestamp))[0]
})
const displayedScanJobId = computed(() => trackedScanJob.value?.jobId)
const scanQueued = computed(() => {
  const status = trackedScanJob.value?.status.toLowerCase()
  return status === 'queued' || status === 'processing'
})
const displayedScanStatus = computed(() => {
  const status = trackedScanJob.value?.status.toLowerCase()
  if (status === 'queued') return 'Queued'
  if (status === 'processing') return 'Processing'
  if (status === 'completed') return 'Completed'
  if (status === 'failed') return 'Failed'
  if (status === 'superseded') return 'Stopped'
  return 'No active scan'
})
const showEditModal = ref(false)
const showOrganizeModal = ref(false)
const showMoreActions = ref(false)

// History state
const historyEntries = ref<History[]>([])
const historyLoading = ref(false)
const historyError = ref<string | null>(null)
const qualityProfiles = ref<import('@/types').QualityProfile[]>([])
const expandedFileAccordions = ref<Set<number>>(new Set())

// Mobile tab options for CustomSelect
const mobileTabOptions = computed(() => [
  { value: 'details', label: 'Details', icon: PhInfo },
  { value: 'files', label: 'Files', icon: PhFile },
  { value: 'chapters', label: 'Chapters', icon: PhListNumbers },
  { value: 'transcript', label: 'Transcript', icon: PhEar },
  { value: 'tags', label: 'Tags', icon: PhTag },
  { value: 'history', label: 'History', icon: PhClockCounterClockwise },
])

const topActions = computed<DetailTopAction[]>(() => [
  {
    key: 'manual-search',
    label: 'Manual Search',
    title: 'Manual Search',
    ariaLabel: 'Manual Search',
    icon: PhMagnifyingGlass,
    desktopGroup: 'primary',
    onClick: openManualSearch,
  },
  {
    key: 'scan',
    label: scanning.value ? 'Scanning...' : scanQueued.value ? 'Scan queued' : 'Scan Folder',
    title: !filesystemReadinessStore.filesystemReady
      ? 'Available after library filesystem initialization completes'
      : scanning.value
        ? 'Scanning...'
        : scanQueued.value
          ? 'Scan queued'
          : "Re-read this book's folder on disk for file changes",
    ariaLabel: 'Scan Folder',
    icon: scanning.value ? PhSpinner : scanQueued.value ? PhClock : PhFileMagnifyingGlass,
    iconClass: scanning.value ? 'ph-spin' : undefined,
    disabled: scanning.value || scanQueued.value || !filesystemReadinessStore.filesystemReady,
    desktopGroup: 'primary',
    onClick: () => {
      void scanFiles()
    },
  },
  {
    key: 'convert',
    label: convertLabel.value,
    title: convertTitle.value,
    ariaLabel: 'Convert to M4B',
    icon: conversionInFlight.value ? PhSpinner : PhFileAudio,
    iconClass: conversionInFlight.value ? 'ph-spin' : undefined,
    disabled: conversionInFlight.value || !hasConvertibleFiles.value,
    desktopGroup: 'secondary',
    onClick: () => {
      void convertToM4b()
    },
  },
  {
    key: 'write-tags',
    label: writeTagsLabel.value,
    title: writeTagsTitle.value,
    ariaLabel: 'Write Metadata Tags',
    icon: tagWriteInFlight.value ? PhSpinner : PhTag,
    iconClass: tagWriteInFlight.value ? 'ph-spin' : undefined,
    disabled: tagWriteInFlight.value || !hasTaggableFiles.value,
    desktopGroup: 'secondary',
    onClick: () => {
      showTagPreviewModal.value = true
    },
  },
  {
    key: 'edit',
    label: 'Edit Metadata',
    title: 'Edit Metadata',
    ariaLabel: 'Edit Metadata',
    icon: PhPencil,
    desktopGroup: 'secondary',
    desktopClass: 'primary',
    onClick: openEditModal,
  },
  {
    key: 'organize',
    label: 'Organize Files',
    title: 'Organize Files',
    ariaLabel: 'Organize Files',
    icon: PhFolderOpen,
    disabled: !audiobook.value?.files?.length && !audiobook.value?.filePath,
    desktopGroup: 'secondary',
    onClick: () => {
      showOrganizeModal.value = true
    },
  },
  {
    key: 'delete',
    label: 'Delete',
    title: 'Delete',
    ariaLabel: 'Delete',
    icon: PhTrash,
    desktopGroup: 'secondary',
    desktopClass: 'danger delete-btn',
    mobileClass: 'delete',
    onClick: confirmDelete,
  },
])

const primaryTopActions = computed(() =>
  topActions.value.filter((a) => a.desktopGroup === 'primary'),
)
const secondaryTopActions = computed(() =>
  topActions.value.filter((a) => a.desktopGroup === 'secondary'),
)

function runTopAction(action: DetailTopAction, closeMoreMenu = false) {
  action.onClick()
  if (closeMoreMenu) {
    showMoreActions.value = false
  }
}

type DetailIdentifierItem = {
  key: string
  type: AudiobookExternalIdentifier['type']
  typeLabel: string
  value: string
  href: string | null
  isPrimary: boolean
}

type DetailTopAction = {
  key:
    | 'refresh'
    | 'manual-search'
    | 'scan'
    | 'monitor'
    | 'edit'
    | 'rescan-metadata'
    | 'convert'
    | 'write-tags'
    | 'organize'
    | 'delete'
  label: string
  title: string
  ariaLabel: string
  icon: Component
  iconClass?: string
  iconProps?: Record<string, unknown>
  disabled?: boolean
  desktopGroup: 'primary' | 'secondary'
  desktopClass?: string
  mobileClass?: string
  onClick: () => void
}

const assignedProfileName = computed(() => {
  if (!audiobook.value) return null
  const id = audiobook.value.qualityProfileId
  if (!id) return null
  const p = qualityProfiles.value.find((q) => q.id === id)
  return p ? p.name : null
})

const primaryAsinIdentifier = computed(() => {
  const ids = audiobook.value?.identifiers || []
  const explicitPrimary = ids.find((id) => id.type === 'Asin' && id.isPrimary && id.value?.trim())
  if (explicitPrimary) return explicitPrimary

  const firstAsin = ids.find((id) => id.type === 'Asin' && id.value?.trim())
  if (firstAsin) return firstAsin

  return null
})

const primaryAsin = computed(() => {
  const identifier = primaryAsinIdentifier.value
  if (identifier?.value?.trim()) return identifier.value.trim()

  const legacy = (audiobook.value?.asin || '').trim()
  return legacy || null
})

const audibleSourceUrl = computed(() => {
  const asin = primaryAsin.value
  if (!asin) return null
  return buildAudibleProductUrl(asin, primaryAsinIdentifier.value?.region ?? undefined)
})

const displayIdentifiers = computed<DetailIdentifierItem[]>(() => {
  const book = audiobook.value
  if (!book) return []

  const items: DetailIdentifierItem[] = []
  const seen = new Set<string>()
  let hasPrimaryAsin = false

  const addIdentifier = (
    type: AudiobookExternalIdentifier['type'],
    rawValue: unknown,
    isPrimary = false,
    rawRegion?: string | null,
  ) => {
    const value = typeof rawValue === 'string' ? rawValue.trim() : ''
    if (!value) return

    const key = normalizeIdentifierKey(type, value)
    if (seen.has(key)) return
    seen.add(key)

    if (type === 'Asin' && isPrimary) hasPrimaryAsin = true

    items.push({
      key,
      type,
      typeLabel: formatIdentifierType(type),
      value,
      href: getIdentifierHref(type, value, rawRegion),
      isPrimary,
    })
  }

  for (const identifier of book.identifiers || []) {
    addIdentifier(
      identifier.type,
      identifier.value,
      Boolean(identifier.isPrimary),
      identifier.region,
    )
  }

  if (book.asin) {
    addIdentifier('Asin', book.asin, !hasPrimaryAsin)
  }

  for (const isbn of getLegacyIsbnValues(book.isbn as unknown)) {
    addIdentifier('Isbn', isbn)
  }

  if (book.openLibraryId) {
    addIdentifier('OpenLibraryId', book.openLibraryId)
  }

  return items.sort((a, b) => {
    const orderDelta = getIdentifierSortOrder(a.type) - getIdentifierSortOrder(b.type)
    if (orderDelta !== 0) return orderDelta
    if (a.isPrimary !== b.isPrimary) return a.isPrimary ? -1 : 1
    return a.value.localeCompare(b.value)
  })
})

/*
 * The series always shows under the title. Audible often sets a book's subtitle to
 * nothing but its series and position ("Sun Eater, Book 3" for a book already filed
 * under Sun Eater #3), and printing that above the chips would say the series twice.
 * Such a subtitle is dropped; one that carries its own meaning ("An Ender Story") is
 * kept and reads above the chips.
 */
const subtitleRestatesSeries = computed(
  () =>
    displaySeriesMemberships.value.length > 0 &&
    isSeriesRestatement(
      audiobook.value?.subtitle || '',
      displaySeriesMemberships.value.map((membership) => membership.seriesName),
    ),
)

const showSubtitle = computed(
  () => Boolean(audiobook.value?.subtitle) && !subtitleRestatesSeries.value,
)

const displaySeriesMemberships = computed<AudiobookSeriesMembership[]>(() => {
  const book = audiobook.value
  if (!book) return []

  const normalized = (book.seriesMemberships || [])
    .map((membership, index) => ({
      ...membership,
      seriesName: (membership.seriesName || '').trim(),
      seriesNumber: membership.seriesNumber?.trim(),
      isPrimary: Boolean(membership.isPrimary),
      sortOrder: typeof membership.sortOrder === 'number' ? membership.sortOrder : index,
    }))
    .filter((membership) => membership.seriesName.length > 0)
    .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))

  if (normalized.length === 0) {
    const legacySeries = (book.series || '').trim()
    if (!legacySeries) return []

    return [
      {
        seriesName: legacySeries,
        seriesNumber: book.seriesNumber?.trim(),
        isPrimary: true,
        sortOrder: 0,
      },
    ]
  }

  if (!normalized.some((membership) => membership.isPrimary) && normalized[0]) {
    normalized[0].isPrimary = true
  }

  return normalized
})

// Utility function to capitalize first letter
const capitalizeFirst = (str: string | undefined): string => {
  if (!str) return ''
  return str.charAt(0).toUpperCase() + str.slice(1).toLowerCase()
}

// Computed property for cover image URL
const coverImageUrl = computed(() => {
  return getProtectedImageSrc(audiobook.value?.imageUrl, getPlaceholderUrl())
})

// Show a base path even when no files exist yet by falling back to configured default root folder
const displayBasePath = computed(() => {
  // Prefer server-provided basePath
  const server = audiobook.value?.basePath
  if (server && server.length > 0) return server

  const settings = configStore.applicationSettings
  if (!settings) return ''

  // Use default root folder path, fallback to legacy outputPath
  const defaultRoot = rootFoldersStore.defaultFolder
  const root = (defaultRoot?.path || settings.outputPath || '').trim()
  const pattern = (settings.folderNamingPattern || settings.fileNamingPattern || '').trim()
  if (!root || !pattern) return root || ''

  const author =
    audiobook.value?.authors && audiobook.value.authors[0]
      ? audiobook.value.authors[0]
      : 'Unknown Author'
  const series = audiobook.value?.series || ''
  const title = audiobook.value?.title || 'Unknown Title'
  const year = audiobook.value?.publishYear || ''
  const seriesNumber = audiobook.value?.seriesNumber || ''

  // Basic variable replacement mirroring server pattern keys
  let relative = pattern
    .replace(/\{Author(?::[^}]+)?\}/gi, sanitizePathComponent(author))
    .replace(/\{Series(?::[^}]+)?\}/gi, sanitizePathComponent(series))
    .replace(/\{Title(?::[^}]+)?\}/gi, sanitizePathComponent(title))
    .replace(/\{Year(?::[^}]+)?\}/gi, year)
    .replace(/\{SeriesNumber(?::[^}]+)?\}/gi, seriesNumber)

  // Remove file-level variables (Disk/Chapter/Quality) if present
  relative = relative
    .replace(/\{DiskNumber(?::[^}]+)?\}/gi, '')
    .replace(/\{ChapterNumber(?::[^}]+)?\}/gi, '')
    .replace(/\{Quality(?::[^}]+)?\}/gi, '')

  // Normalize repeated slashes and trim
  relative = relative.replace(/[\\/]{2,}/g, '/').replace(/^\/+|\/+$/g, '')

  const combined = joinPaths(root, relative)
  // Base path should be the directory containing the files -> strip the last segment
  const parts = combined.split(/[/\\]+/).filter(Boolean)
  if (parts.length <= 1) return combined
  const dir = parts.slice(0, -1).join('/')
  return dir
})

function sanitizePathComponent(s?: string): string {
  if (!s) return 'Unknown'
  // Replace invalid filename chars with underscore
  return s.replace(/[\\/:*?"<>|]/g, '_').trim() || 'Unknown'
}

function getLegacyIsbnValues(raw: unknown): string[] {
  if (Array.isArray(raw)) {
    return raw.map((value) => (typeof value === 'string' ? value.trim() : '')).filter(Boolean)
  }

  if (typeof raw !== 'string') return []

  return raw
    .split(',')
    .map((value) => value.trim())
    .filter(Boolean)
}

function formatIdentifierType(type: AudiobookExternalIdentifier['type']): string {
  if (type === 'Asin') return 'ASIN'
  if (type === 'Isbn') return 'ISBN'
  return 'Open Library'
}

function getIdentifierSortOrder(type: AudiobookExternalIdentifier['type']): number {
  if (type === 'Asin') return 0
  if (type === 'Isbn') return 1
  return 2
}

function normalizeIdentifierKey(type: AudiobookExternalIdentifier['type'], value: string): string {
  const normalizedValue =
    type === 'Isbn' ? value.replace(/[-\s]/g, '').toUpperCase() : value.trim().toUpperCase()
  return `${type}:${normalizedValue}`
}

function getIdentifierHref(
  type: AudiobookExternalIdentifier['type'],
  value: string,
  region?: string | null,
): string | null {
  if (type === 'Asin') {
    return buildAudibleProductUrl(value, region ?? undefined)
  }

  if (type === 'OpenLibraryId') {
    const trimmed = value.trim()
    if (!trimmed) return null
    if (/^https?:\/\//i.test(trimmed)) return trimmed
    const normalized = trimmed.replace(/^\/+/, '')
    return `https://openlibrary.org/books/${encodeURIComponent(normalized)}`
  }

  return null
}

function normalizeDetailTabCandidate(value: unknown): DetailTab | null {
  if (typeof value !== 'string') return null
  const normalized = value.trim().toLowerCase()
  if (normalized === 'downloads') return 'history'
  if (
    normalized === 'details' ||
    normalized === 'files' ||
    normalized === 'chapters' ||
    normalized === 'transcript' ||
    normalized === 'tags' ||
    normalized === 'history'
  ) {
    return normalized
  }
  return null
}

function syncActiveTabFromRoute() {
  const fromQuery = normalizeDetailTabCandidate(route.query?.tab)
  if (fromQuery) {
    activeTab.value = fromQuery
    return
  }

  const fromHash = normalizeDetailTabCandidate((route.hash || '').replace(/^#/, ''))
  if (fromHash) {
    activeTab.value = fromHash
  }
}

// Watch for tab changes to load history when needed
watch(activeTab, async (newTab) => {
  if (newTab === 'history' && audiobook.value && historyEntries.value.length === 0) {
    await loadHistory()
  }
  try {
    history.replaceState(null, '', `#${newTab}`)
  } catch {}
})

// Handle dropdown tab change
// const onTabChange = (event: Event) => {
//   const target = event.target as HTMLSelectElement
//   const newTab = target.value as 'details' | 'files' | 'history'
//   activeTab.value = newTab
// }

let audiobookUpdateUnsub: (() => void) | null = null
let scanJobUpdateUnsub: (() => void) | null = null

onMounted(async () => {
  syncActiveTabFromRoute()
  document.addEventListener('click', handleClickOutside)

  // Idempotent: the store subscribes once and the Activity view may have started
  // it already. Without it the button cannot tell that a conversion is running.
  conversionJobsStore.start()
  tagJobsStore.start()

  // The Transcribe button reads the transcription setting; App.vue may not have
  // loaded settings yet on a deep link.
  if (!configStore.applicationSettings) {
    void configStore.loadApplicationSettings()
  }

  await loadAudiobook()

  // Keep the shared scan notification store current when this detail view is mounted.
  // App.vue also subscribes globally; duplicate updates are monotonic/idempotent in the store.
  scanJobUpdateUnsub = signalRService.onScanJobUpdate((job) => {
    if (!audiobook.value) return
    if (String(job.audiobookId) !== String(audiobook.value.id)) return
    scanNotificationsStore.applyUpdate(job)
  })

  // subscribe to AudiobookUpdate messages and merge detail when this audiobook is updated (e.g., after a move)
  audiobookUpdateUnsub = signalRService.onAudiobookUpdate(async (updated) => {
    if (!audiobook.value) return
    const updatedAudiobook = updated as unknown as import('@/types').Audiobook | undefined
    if (!updatedAudiobook || String(updatedAudiobook.id) !== String(audiobook.value.id)) return

    // Merge server-provided audiobook fields into local detail object to update instantly without reloading
    try {
      const upd = updated as unknown as import('@/types').Audiobook
      const prev = audiobook.value
      if (!prev) return

      // Create merged object, preferring server values when provided
      const merged = { ...prev, ...upd }

      // Replace files array only when server provides non-empty array (prevents accidental clearing)
      if (upd.files && upd.files.length > 0) {
        merged.files = upd.files
      }

      // Preserve basePath if server omitted it or sent empty
      if ((!('basePath' in upd) || !upd.basePath) && prev.basePath) {
        merged.basePath = prev.basePath
      }

      // Apply merged object reactively
      audiobook.value = merged
    } catch {
      // Fallback: if merge fails, try a full reload
      setTimeout(async () => {
        try {
          await loadAudiobook()
        } catch {}
      }, 250)
    }
  })
})

function handleClickOutside() {
  if (showMoreActions.value) {
    showMoreActions.value = false
  }
}

onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside)
  try {
    if (audiobookUpdateUnsub) audiobookUpdateUnsub()
  } catch {}
  try {
    if (scanJobUpdateUnsub) scanJobUpdateUnsub()
  } catch {}
})

watch(
  () => [route.hash, route.query?.tab],
  () => {
    syncActiveTabFromRoute()
  },
)

async function loadAudiobook() {
  loading.value = true
  error.value = null

  try {
    const id = parseInt(route.params.id as string)
    let loadedBook: Audiobook | null = null

    // Prefer the dedicated detail endpoint when available.
    if (typeof apiService.getAudiobook === 'function') {
      try {
        loadedBook = await apiService.getAudiobook(id)
      } catch (apiErr) {
        logger.debug('Detail endpoint load failed, falling back to library store', apiErr)
      }
    }

    if (!loadedBook) {
      // Fallback path for tests / older mocks / endpoint failures
      if (libraryStore.audiobooks.length === 0) {
        await libraryStore.fetchLibrary()
      }
      loadedBook = libraryStore.audiobooks.find((b) => b.id === id) || null
    }

    if (loadedBook) {
      audiobook.value = loadedBook
      await afterLoad()
    } else {
      error.value = 'Audiobook not found'
    }
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Failed to load audiobook'
    errorTracking.captureException(err as Error, {
      component: 'AudiobookDetailView',
      operation: 'loadAudiobook',
      metadata: { audiobookId: route.params.id },
    })
  } finally {
    loading.value = false
  }
}

// After loading audiobook, also fetch quality profiles so we can display the assigned profile
async function afterLoad() {
  await loadQualityProfilesForDetail()
  await loadIdentifiersForDetail()
  try {
    const img = audiobook.value?.imageUrl
    if (img) {
      const url = apiService.getImageUrl(img)
      if (url && isApiImagesUrl(url)) {
        // fire-and-forget: ensure backend cached copy exists for this image
        void ensureImageCached(url).catch(() => {})
      }
    }
  } catch {}
}

async function loadQualityProfilesForDetail() {
  try {
    qualityProfiles.value = await apiService.getQualityProfiles()
  } catch (err) {
    logger.warn('Failed to load quality profiles for detail view:', err)
  }
}

async function loadIdentifiersForDetail() {
  const id = audiobook.value?.id
  if (!id || typeof apiService.getAudiobookIdentifiers !== 'function') return

  try {
    const response = await apiService.getAudiobookIdentifiers(id)
    if (!audiobook.value || audiobook.value.id !== id) return
    audiobook.value = {
      ...audiobook.value,
      identifiers: Array.isArray(response?.identifiers) ? response.identifiers : [],
    }
  } catch (err) {
    logger.debug('Failed to load audiobook identifiers for detail view', err)
  }
}

function goBack() {
  router.push('/books')
}

function goToAuthorCollection(author: string | undefined | null) {
  const normalizedAuthor = author?.trim()
  if (!normalizedAuthor) return

  router.push(`/collection/author/${encodeURIComponent(normalizedAuthor)}`)
}

function goToNarratorCollection(narrator: string | undefined | null) {
  const normalizedNarrator = narrator?.trim()
  if (!normalizedNarrator) return

  router.push(`/collection/narrator/${encodeURIComponent(normalizedNarrator)}`)
}

function goToPublisherCollection(publisher: string | undefined | null) {
  const normalizedPublisher = publisher?.trim()
  if (!normalizedPublisher) return

  router.push(`/collection/publisher/${encodeURIComponent(normalizedPublisher)}`)
}

function goToSeriesCollection(series: string | undefined | null) {
  const normalizedSeries = series?.trim()
  if (!normalizedSeries) return

  router.push(`/collection/series/${encodeURIComponent(normalizedSeries)}`)
}

function goToGenreCollection(genre: string | undefined | null) {
  const normalizedGenre = genre?.trim()
  if (!normalizedGenre) return

  router.push(`/collection/genre/${encodeURIComponent(normalizedGenre)}`)
}

/**
 * Anything conversion would read: any audio file that is not already an M4B, which
 * is the format it produces. A book that is already M4B has nothing to gain, so the
 * button is disabled rather than offering work that would be refused.
 */
const CONVERTIBLE = /\.(mp3|mp4|m4a|flac|ogg|opus|aac|wav|wv|wma|ape|alac|aiff?)$/i
const hasConvertibleFiles = computed(
  () => audiobook.value?.files?.some((file) => CONVERTIBLE.test(file.path ?? '')) ?? false,
)

const activeConversion = computed(() =>
  audiobook.value ? conversionJobsStore.getJobForAudiobook(audiobook.value.id) : undefined,
)

const conversionInFlight = computed(() => {
  const status = activeConversion.value?.status
  return status === 'Queued' || status === 'Running' || status === 'RetryScheduled'
})

const convertLabel = computed(() => {
  const job = activeConversion.value
  if (job?.status === 'Running') {
    return `Converting ${Math.round(job.progress)}%`
  }

  return conversionInFlight.value ? 'Conversion queued' : 'Convert to M4B'
})

const convertTitle = computed(() => {
  if (!hasConvertibleFiles.value) {
    return 'This book has no MP3 files to convert'
  }

  return conversionInFlight.value
    ? 'A conversion is already queued for this book'
    : "Fold this book's MP3 files into a single M4B with chapters"
})

async function convertToM4b() {
  if (!audiobook.value || conversionInFlight.value) return

  const toast = useToast()
  try {
    const response = await conversionJobsStore.convert(audiobook.value.id)
    if (response.queued) {
      toast.success(
        'Conversion queued',
        'Progress is shown in Activity. The original files are left alone until the result is verified.',
      )
    } else {
      // A refusal carries its reason from the server; showing it beats a generic
      // failure the operator cannot act on.
      toast.error('Not queued', response.reason ?? 'This book could not be queued for conversion.')
    }
  } catch (err) {
    errorTracking.captureException(err as Error, {
      component: 'AudiobookDetailView',
      operation: 'convertToM4b',
      metadata: { audiobookId: audiobook.value?.id },
    })
    toast.error('Conversion failed to queue', err instanceof Error ? err.message : String(err))
  }
}

const showTagPreviewModal = ref(false)

/**
 * The Tags tab's panel, held so a finished write can re-read the files. The values it
 * shows are read off disk, so leaving them as they were after a successful write would
 * show the old tags beside a book that no longer has them.
 */
const tagsPanel = ref<InstanceType<typeof AudiobookTagsPanel> | null>(null)

const hasTaggableFiles = computed(
  () =>
    audiobook.value?.files?.some((file) => {
      const path = (file.path ?? '').toLowerCase()
      return path.endsWith('.m4b') || path.endsWith('.m4a')
    }) ?? false,
)

const activeTagWrite = computed(() =>
  audiobook.value ? tagJobsStore.getJobForAudiobook(audiobook.value.id) : undefined,
)

const tagWriteInFlight = computed(() => {
  const status = activeTagWrite.value?.status
  return status === 'Queued' || status === 'Running' || status === 'RetryScheduled'
})

const writeTagsLabel = computed(() => {
  const job = activeTagWrite.value
  if (job?.status === 'Running') {
    return `Writing tags ${Math.round(job.progress)}%`
  }

  return tagWriteInFlight.value ? 'Tag write queued' : 'Write Tags'
})

const writeTagsTitle = computed(() => {
  if (!hasTaggableFiles.value) {
    // MP3 books are converted first: ID3 cannot carry the description atom that is
    // the whole point of writing tags.
    return 'This book has no M4B files to write tags into'
  }

  return tagWriteInFlight.value
    ? 'A tag write is already queued for this book'
    : "Write the library's metadata into this book's M4B files"
})

async function writeTags(payload: { tags: string[]; values: Record<string, string> }) {
  if (!audiobook.value) return

  showTagPreviewModal.value = false
  const toast = useToast()
  try {
    const response = await tagJobsStore.write(audiobook.value.id, payload.tags, payload.values)
    if (response.queued) {
      toast.success(
        'Tag write queued',
        'Progress is shown in Activity. Each file is copied, tagged and checked before it replaces the original.',
      )
    } else {
      // A refusal carries its reason from the server; showing it beats a generic
      // failure the operator cannot act on.
      toast.error('Not queued', response.reason ?? 'This book could not be queued for tag writing.')
    }
  } catch (err) {
    errorTracking.captureException(err as Error, {
      component: 'AudiobookDetailView',
      operation: 'writeTags',
      metadata: { audiobookId: audiobook.value?.id },
    })
    toast.error('Tag write failed to queue', err instanceof Error ? err.message : String(err))
  }
}

/**
 * How many of this book's jobs of each kind have finished. A count, not a status
 * string: a second run of the same kind must register even while the first still
 * sits completed in the store.
 */
function completedCount(kinds: string[]) {
  return tagJobsStore.jobs.filter(
    (job) =>
      job.audiobookId === audiobook.value?.id &&
      kinds.includes(job.kind) &&
      job.status === 'Completed',
  ).length
}

watch(
  () => completedCount(['Tags']),
  (count, previous) => {
    if (previous !== undefined && count > previous) {
      void tagsPanel.value?.load()
    }
  },
)

// A chapter repair replaced a file and re-judged it; a plan job stored its proposal.
// Either way the badge and the panel are whatever the server now says.
watch(
  () => completedCount(['Chapters', 'Plan']),
  (count, previous) => {
    if (previous !== undefined && count > previous) {
      void loadAudiobook()
      void chapterPanel.value?.load()
    }
  },
)

function onReplan(payload: { fileId: number; queued: boolean; reason?: string }) {
  const toast = useToast()
  if (payload.queued) {
    toast.success(
      'Re-checking',
      'The fix is being worked out again; this tab refreshes when it is ready.',
    )
  } else {
    toast.error('Not re-checked', payload.reason ?? 'The fix could not be queued for planning.')
  }
}

/** The badge text for a file whose chapters a repair can do something about, or null. */
function chapterBadge(health: ChapterHealth | undefined): string | null {
  switch (health) {
    case 'corrupt':
      return 'Corrupt chapters'
    case 'oversegmented':
      return 'CD-track chapters'
    case 'generic-titles':
      return 'Unnamed chapters'
    case 'none':
      return 'No chapters'
    default:
      return null
  }
}

// ---- audio audit -----------------------------------------------------------------

const audioAuditLabel = computed(() => {
  switch (audiobook.value?.audioAudit) {
    case 'match':
      return 'The audio introduces itself as this book.'
    case 'narrator-mismatch':
      return 'Right book, different narrator.'
    case 'incomplete':
      return 'Right book, but the audio stops mid-sentence.'
    case 'mismatch':
      return 'The audio introduces itself as a different book.'
    case 'inconclusive':
      return 'Could not tell from the audio.'
    default:
      return ''
  }
})

const CLOSING_MARKER = '\n\n[closing]\n'

const heardParts = computed(() => {
  const heard = audiobook.value?.audioAuditHeard ?? ''
  const at = heard.indexOf(CLOSING_MARKER)
  const opening = at >= 0 ? heard.slice(0, at) : heard
  const closing = at >= 0 ? heard.slice(at + CLOSING_MARKER.length) : ''
  const flatten = (text: string) => text.replace(/\s*\n\s*/g, ' ').trim()
  return { opening: flatten(opening), closing: flatten(closing) }
})
const heardOpening = computed(() => heardParts.value.opening)
const heardClosing = computed(() => heardParts.value.closing)

function formatAuditedAt(iso: string) {
  const when = new Date(iso)
  return Number.isNaN(when.getTime()) ? '' : when.toLocaleString()
}

const settingNarrator = ref(false)

// ---- not-found files ---------------------------------------------------------------

const notFoundFiles = computed(() =>
  (audiobook.value?.files ?? []).filter((f) => !!f.notFoundSince),
)
const removingNotFound = ref(false)

function formatNotFoundSince(iso: string) {
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString()
}

async function removeNotFoundFiles() {
  if (!audiobook.value || notFoundFiles.value.length === 0) return
  const count = notFoundFiles.value.length
  const ok = await showConfirm(
    `Remove ${count} file record${count === 1 ? '' : 's'} a scan could not find? ` +
      'Nothing on disk is touched; the book simply stops tracking these files. If the ' +
      'files are only temporarily away — a share that has not mounted — a scan will clear ' +
      'the flag instead.',
    'Remove not-found files',
    { confirmText: 'Remove', cancelText: 'Cancel', danger: true },
  )
  if (!ok) return
  removingNotFound.value = true
  const toast = useToast()
  try {
    const result = await apiService.removeNotFoundFiles(audiobook.value.id)
    toast.success(
      'Files removed',
      `${result.removed} file record${result.removed === 1 ? '' : 's'} removed.`,
    )
    await loadAudiobook()
  } catch (err) {
    toast.error('Could not remove files', err instanceof Error ? err.message : String(err))
  } finally {
    removingNotFound.value = false
  }
}

/** Put the narrator the audio names on the record, then transcribe again to confirm. */
async function adoptHeardNarrator() {
  if (!audiobook.value?.audioAuditHeardNarrator) return
  settingNarrator.value = true
  const toast = useToast()
  try {
    // The stored name is already spelled the way the library spells it, and several
    // readers arrive comma-separated; "and" and "&" are still split for a transcript
    // taken before that correction existed.
    const narrators = audiobook.value.audioAuditHeardNarrator
      .split(/\s*(?:,|&|\band\b)\s*/i)
      .map((n) => n.trim())
      .filter(Boolean)
    await apiService.updateAudiobook(audiobook.value.id, { narrators })
    toast.success('Narrator updated', `Set to ${narrators.join(', ')}.`)
    await loadAudiobook()
    await auditAudio()
  } catch (err) {
    toast.error('Could not update the narrator', err instanceof Error ? err.message : String(err))
  } finally {
    settingNarrator.value = false
  }
}

const acceptingAudio = ref(false)

/**
 * Say the record is right in spite of the verdict, or take that back.
 *
 * The verdict is kept and still shown; the book simply stops being counted as a
 * problem. Pinned to the files as they are, so a recording swapped in later is judged
 * on its own and the flag returns.
 */
async function setAudioAccepted(accepted: boolean) {
  if (!audiobook.value) return
  acceptingAudio.value = true
  const toast = useToast()
  try {
    await apiService.setAudioAuditAccepted(audiobook.value.id, accepted)
    toast.success(
      accepted ? 'Kept as recorded' : 'No longer accepted',
      accepted
        ? 'This book will not be flagged again unless its files change.'
        : 'The verdict counts against this book again.',
    )
    await loadAudiobook()
  } catch (err) {
    toast.error('Could not save that', err instanceof Error ? err.message : String(err))
  } finally {
    acceptingAudio.value = false
  }
}

/**
 * What to do about the verdict. Each is a sentence and, where one exists, the action:
 * a wrong book is re-matched from what the narrator said, a wrong narrator is adopted,
 * and a verdict the listener disagrees with is overruled.
 */
const creditRecommendations = computed(() => {
  const book = audiobook.value
  if (!book?.audioAudit) return []
  const recs: { text: string; label?: string; action?: () => void; busy?: boolean }[] = []
  switch (book.audioAudit) {
    case 'mismatch':
      recs.push({
        text: book.audioAuditHeardTitle
          ? `The audio introduces itself as “${book.audioAuditHeardTitle}”${book.audioAuditHeardAuthor ? ` by ${book.audioAuditHeardAuthor}` : ''}. Re-match this record to that edition.`
          : 'Neither the title nor the author on record was heard. Re-match this record, or check the files are the right book.',
        label: 'Fix match…',
        action: openFixMatchFromAudit,
      })
      break
    case 'narrator-mismatch':
      recs.push({
        text: `The audio names ${book.audioAuditHeardNarrator} as narrator, not ${(book.narrators || []).join(' / ')}. If the audio is right, put that narrator on the record.`,
        label: `Set narrator to ${book.audioAuditHeardNarrator}`,
        action: adoptHeardNarrator,
        busy: settingNarrator.value,
      })
      recs.push({
        text: 'If the record is right and this is a different edition, re-match it.',
        label: 'Fix match…',
        action: openFixMatchFromAudit,
      })
      break
    case 'incomplete':
      recs.push({
        text: `${book.audioAuditReason ?? 'The audio stops mid-sentence rather than finishing.'} A finished production reads its closing credits; this one simply stops, which means the file is cut off rather than being a shorter edition. Download it again.`,
        label: 'Fix match…',
        action: openFixMatchFromAudit,
      })
      break
    case 'inconclusive':
      recs.push({
        text: 'Too little was heard to tell: the opening may be music, or the title and author may be read later than the first minute. Transcribing again after choosing a larger model in Settings → Metadata Tags can help.',
      })
      break
    default:
      break
  }

  // Last, because it is the answer only once the others have been considered: the audit
  // can be wrong - an opening under music, a transcript that loops - and without this the
  // same book is offered for repair for ever.
  if (
    book.audioAudit === 'mismatch' ||
    book.audioAudit === 'narrator-mismatch' ||
    book.audioAudit === 'incomplete'
  ) {
    recs.push(
      book.audioAuditAccepted
        ? {
            text: 'You listened to this and kept it as recorded, so it is not counted as a problem. It will be flagged again if the files change.',
            label: 'Flag it again',
            action: () => setAudioAccepted(false),
            busy: acceptingAudio.value,
          }
        : {
            text: 'If you have listened for yourself and the record is right, keep it as it is.',
            label: 'Sounds right',
            action: () => setAudioAccepted(true),
            busy: acceptingAudio.value,
          },
    )
  }

  return recs
})

/** Known off, as against not yet loaded: the button is only disabled on a definite no. */
const transcriptionOff = computed(
  () => configStore.applicationSettings?.transcriptionEnabled === false,
)

const audioAuditInFlight = computed(() => {
  const job = tagJobsStore.jobs.find(
    (candidate) =>
      candidate.audiobookId === audiobook.value?.id &&
      candidate.kind === 'Audit' &&
      (candidate.status === 'Queued' ||
        candidate.status === 'Running' ||
        candidate.status === 'RetryScheduled'),
  )
  return !!job
})

async function auditAudio() {
  if (!audiobook.value) return
  const toast = useToast()
  try {
    const response = await tagJobsStore.auditAudio(audiobook.value.id)
    if (response.queued) {
      toast.success(
        'Transcribing',
        'The verdict appears on the Transcript tab once the opening and closing have been heard.',
      )
    } else {
      toast.error('Not queued', response.reason ?? 'This book could not be queued for an audit.')
    }
  } catch (err) {
    errorTracking.captureException(err as Error, {
      component: 'AudiobookDetailView',
      operation: 'auditAudio',
      metadata: { audiobookId: audiobook.value?.id },
    })
    toast.error('Audit failed to queue', err instanceof Error ? err.message : String(err))
  }
}

/** Start the re-match from what the narrator said the book is, not from the record that may be wrong. */
const fixMatchQuery = ref<string | null>(null)
const fixMatchAuthor = ref<string | null>(null)

function openFixMatchFromAudit() {
  fixMatchQuery.value = audiobook.value?.audioAuditHeardTitle ?? null
  fixMatchAuthor.value = audiobook.value?.audioAuditHeardAuthor ?? null
  showFixMatchModal.value = true
}

// An audit that finishes rewrites the verdict on the book; reload to show it.
watch(
  () => completedCount(['Audit']),
  (count, previous) => {
    if (previous !== undefined && count > previous) {
      void loadAudiobook()
    }
  },
)

const showChapterRepairModal = ref(false)
const chapterRepairScopes = ref<ChapterRepairScope[]>([])

function openChapterRepair(...fileIds: number[]) {
  if (!audiobook.value || fileIds.length === 0) return
  chapterRepairScopes.value = [
    { audiobookId: audiobook.value.id, title: audiobook.value.title ?? '', fileIds },
  ]
  showChapterRepairModal.value = true
}

// ---- chapter check ---------------------------------------------------------------

const CHAPTER_ISSUE_HEALTH = new Set<ChapterHealth>([
  'corrupt',
  'oversegmented',
  'generic-titles',
  'none',
])

/** One line for the whole book, from the worst of its files. */
const chapterSummary = computed(() => {
  const files = audiobook.value?.files ?? []
  const taggable = files.filter((f) => /\.(m4b|m4a)$/i.test(f.path ?? ''))
  const judged = taggable.filter((f) => f.chapterHealth && f.chapterHealth !== 'unknown')
  const repairableFileIds = judged
    .filter((f) => CHAPTER_ISSUE_HEALTH.has(f.chapterHealth as ChapterHealth))
    .map((f) => f.id)

  if (taggable.length === 0) {
    return {
      checked: false,
      severity: 'none' as const,
      headline: 'Chapters are only inspected in M4B files.',
      detail: 'Convert this book to M4B to have its chapter structure checked and repaired.',
      repairableFileIds,
    }
  }

  if (judged.length === 0) {
    return {
      checked: false,
      severity: 'none' as const,
      headline: 'Chapters not yet checked.',
      detail:
        'A check reads each file’s chapter atom and chapter track and reports a broken atom, CD-track splits, or placeholder titles.',
      repairableFileIds,
    }
  }

  const worst = judged
    .map((f) => f.chapterHealth as ChapterHealth)
    .sort((a, b) => severityRank(b) - severityRank(a))[0]
  const reasons = judged
    .filter((f) => f.chapterHealth === worst)
    .map((f) => f.chapterReason)
    .filter((r): r is string => !!r)
  const count = judged.reduce((sum, f) => sum + (f.chapterCount ?? 0), 0)

  // Amber when every flagged file can be fixed automatically, red when one cannot.
  const flagged = judged.filter((f) => CHAPTER_ISSUE_HEALTH.has(f.chapterHealth as ChapterHealth))
  const fixable = flagged.length > 0 && flagged.every((f) => f.chapterRepairable)
  const severity = fixable ? ('fixable' as const) : ('issue' as const)
  const fixNote = fixable
    ? ' A repair can fix this automatically.'
    : ' No automatic fix is available yet: the book needs an ASIN match, or transcription turned on, for a repair to have a source.'

  switch (worst) {
    case 'corrupt':
      return {
        checked: true,
        severity,
        headline: 'Corrupt chapter atom.',
        detail: (reasons[0] ?? '') + fixNote,
        repairableFileIds,
      }
    case 'oversegmented':
      return {
        checked: true,
        severity,
        headline: 'Chapters are CD tracks.',
        detail: (reasons[0] ?? '') + fixNote,
        repairableFileIds,
      }
    case 'generic-titles':
      return {
        checked: true,
        severity,
        headline: 'Chapters have placeholder titles.',
        detail: (reasons[0] ?? '') + fixNote,
        repairableFileIds,
      }
    case 'none':
      return {
        checked: true,
        severity,
        headline: 'No chapter marks.',
        detail: (reasons[0] ?? '') + fixNote,
        repairableFileIds,
      }
    default:
      return {
        checked: true,
        severity: 'ok' as const,
        headline: `Chapters look right (${count} across ${judged.length} file${judged.length === 1 ? '' : 's'}).`,
        detail: 'The chapter atom parses and agrees with what the file plays.',
        repairableFileIds,
      }
  }
})

function severityRank(health: ChapterHealth): number {
  switch (health) {
    case 'corrupt':
      return 5
    case 'oversegmented':
      return 4
    case 'generic-titles':
      return 3
    case 'none':
      return 2
    case 'healthy':
      return 1
    default:
      return 0
  }
}

/**
 * A mark on the Chapters tab before it is opened: how many files a repair has
 * something to do for, red when the marks themselves are wrong and amber when only
 * the names are. From the verdicts the server already holds, so it costs nothing.
 */
const chapterTabBadge = computed(() => {
  const files = (audiobook.value?.files ?? []).filter((f) =>
    CHAPTER_ISSUE_HEALTH.has(f.chapterHealth as ChapterHealth),
  )
  if (files.length === 0) return null
  // Amber: every flagged file can be fixed automatically. Red: at least one cannot.
  const stuck = files.filter((f) => !f.chapterRepairable).length
  return stuck > 0
    ? {
        severity: 'issue',
        title: `${stuck} of ${files.length} file(s) have chapter problems that cannot be fixed automatically — open Chapters to see why`,
      }
    : {
        severity: 'note',
        title: `${files.length} file(s) have chapter problems a repair can fix — open Chapters to preview it`,
      }
})
const audioTabBadge = computed(() => {
  switch (audiobook.value?.audioAudit) {
    case 'mismatch':
      return 'The audio introduces itself as a different book'
    case 'narrator-mismatch':
      return 'The audio names a different narrator'
    case 'incomplete':
      return 'The audio stops mid-sentence rather than finishing'
    default:
      return null
  }
})

const chapterPanelClass = computed(() => {
  switch (chapterSummary.value.severity) {
    case 'issue':
      return 'audio-audit--mismatch'
    case 'fixable':
      return 'audio-audit--fixable'
    case 'ok':
      return 'audio-audit--match'
    default:
      return 'audio-audit--none'
  }
})

const chapterPanel = ref<InstanceType<typeof ChapterListPanel> | null>(null)
const checkingChapters = ref(false)

/**
 * Judging is what the tag table does when it loads: probe the files (cached by size
 * and mtime) and record the verdict on each. Asking for this one book does the same
 * for it alone and does not clear anything else's cache.
 */
async function checkChapters() {
  if (!audiobook.value) return
  checkingChapters.value = true
  const toast = useToast()
  try {
    await apiService.getLibraryTags(false, [audiobook.value.id])
    await loadAudiobook()
    await chapterPanel.value?.load()
  } catch (err) {
    toast.error('Could not check chapters', err instanceof Error ? err.message : String(err))
  } finally {
    checkingChapters.value = false
  }
}

async function repairChapters(books: { audiobookId: number; fileIds: number[] }[]) {
  showChapterRepairModal.value = false
  const toast = useToast()
  for (const book of books) {
    try {
      const response = await tagJobsStore.repairChapters(book.audiobookId, book.fileIds)
      if (response.queued) {
        toast.success(
          'Chapter repair queued',
          'Progress is shown in Activity. The file is remuxed, re-tagged and checked before it replaces the original.',
        )
      } else {
        toast.error('Not queued', response.reason ?? 'This file could not be queued for repair.')
      }
    } catch (err) {
      errorTracking.captureException(err as Error, {
        component: 'AudiobookDetailView',
        operation: 'repairChapters',
        metadata: { audiobookId: book.audiobookId },
      })
      toast.error(
        'Chapter repair failed to queue',
        err instanceof Error ? err.message : String(err),
      )
    }
  }
}

// ---- which edition is this -------------------------------------------------------

const editionCheck = ref<EditionCheck | null>(null)
const checkingEdition = ref(false)
const applyingEdition = ref(false)

const editionNarrators = computed(() =>
  (editionCheck.value?.best?.narrators || []).join(' / ') || 'other',
)

async function runEditionCheck() {
  if (!audiobook.value || checkingEdition.value) return
  checkingEdition.value = true
  editionCheck.value = null
  try {
    editionCheck.value = await apiService.checkEdition(audiobook.value.id)
  } catch (err) {
    useToast().error(
      'Could not check the edition',
      err instanceof Error ? err.message : String(err),
    )
  } finally {
    checkingEdition.value = false
  }
}

// Adopting an edition is the same two steps as Fix match: point the record at that
// identifier, then let the ordinary rescan bring the rest across. Locked fields
// survive it as they would any rescan.
async function adoptEdition() {
  const asin = editionCheck.value?.best?.asin
  if (!audiobook.value || !asin || applyingEdition.value) return
  applyingEdition.value = true
  try {
    const others = (audiobook.value.identifiers || [])
      .filter((identifier) => identifier.type !== 'Asin')
      .map((identifier) => ({
        type: identifier.type,
        value: identifier.value,
        region: identifier.region,
        isPrimary: false,
        source: identifier.source,
      }))
    await apiService.updateAudiobookIdentifiers(audiobook.value.id, [
      { type: 'Asin', value: asin, isPrimary: true, source: 'Manual' },
      ...others,
    ])
    await rescanMetadata()
    editionCheck.value = null
  } catch (err) {
    useToast().error('Could not re-match', err instanceof Error ? err.message : String(err))
  } finally {
    applyingEdition.value = false
  }
}

const showFixMatchModal = ref(false)

function closeFixMatch() {
  showFixMatchModal.value = false
  fixMatchQuery.value = null
  fixMatchAuthor.value = null
}

// A wrong match is fixed by pointing the book at the right edition and letting the
// ordinary rescan do the rest; locked fields survive it as they would any rescan.
async function applyMatch(result: SearchResult) {
  showFixMatchModal.value = false
  if (!audiobook.value) return
  // Whatever identifies the picked edition: an ASIN from Audible, else an
  // OpenLibrary work. It replaces the same kind of identifier the book had.
  const asin = (result.asin || '').trim()
  // An OpenLibrary result names its work only by link.
  const openLibraryId = result.link?.match(/openlibrary\.org\/(?:works|books)\/(OL\w+)/)?.[1] ?? ''
  const chosen: AudiobookExternalIdentifierInput | null = asin
    ? { type: 'Asin', value: asin, isPrimary: true, source: 'Manual' }
    : openLibraryId
      ? { type: 'OpenLibraryId', value: openLibraryId, isPrimary: true, source: 'Manual' }
      : null
  if (!chosen) {
    useToast().error('No identifier', 'That result carries nothing to match against.')
    return
  }

  try {
    const others = (audiobook.value.identifiers || [])
      .filter((identifier) => identifier.type !== chosen.type)
      .map((identifier) => ({
        type: identifier.type,
        value: identifier.value,
        region: identifier.region,
        isPrimary: false,
        source: identifier.source,
      }))
    await apiService.updateAudiobookIdentifiers(audiobook.value.id, [chosen, ...others])
  } catch (err) {
    useToast().error('Could not change the match', err instanceof Error ? err.message : String(err))
    return
  }

  if (chosen.type !== 'Asin') {
    // The rescan follows ASINs and ISBNs; an OpenLibrary work is recorded but cannot
    // refresh the metadata on its own.
    await loadAudiobook()
    useToast().info(
      'Identifier saved',
      'Metadata is refreshed from Audible or an ISBN; search by ASIN or ISBN to re-match fully.',
    )
    return
  }

  await rescanMetadata()
}

async function rescanMetadata() {
  if (!audiobook.value || rescanningMetadata.value) return

  rescanningMetadata.value = true
  const toast = useToast()
  try {
    const response = await apiService.rescanAudiobookMetadata(audiobook.value.id)
    await loadAudiobook()

    const details: string[] = []
    if (response?.source) details.push(`Source: ${response.source}`)
    if (response?.asin) details.push(`ASIN: ${response.asin}`)

    // Pinned fields are named, not counted. A rescan that looks like it changed nothing
    // should say which locks are the reason before the operator goes hunting for a bug.
    if (response?.keptFields?.length) {
      details.push(`Kept your ${response.keptFields.join(', ')}`)
    }

    toast.success(
      'Metadata rescanned',
      details.length > 0 ? details.join(' • ') : 'Audiobook metadata refreshed successfully.',
    )
  } catch (err) {
    errorTracking.captureException(err as Error, {
      component: 'AudiobookDetailView',
      operation: 'rescanMetadata',
      metadata: { audiobookId: audiobook.value?.id },
    })
    toast.error('Metadata rescan failed', err instanceof Error ? err.message : String(err))
  } finally {
    rescanningMetadata.value = false
  }
}

async function loadHistory() {
  if (!audiobook.value) return

  historyLoading.value = true
  historyError.value = null

  try {
    historyEntries.value = await apiService.getHistoryByAudiobookId(audiobook.value.id)
    logger.debug('Loaded history:', historyEntries.value)
  } catch (err) {
    historyError.value = err instanceof Error ? err.message : 'Failed to load history'
    logger.error('Failed to load history:', err)
  } finally {
    historyLoading.value = false
  }
}

function openManualSearch() {
  showManualSearchModal.value = true
}

function closeManualSearch() {
  showManualSearchModal.value = false
}

function handleDownloaded(result: SearchResult) {
  logger.debug('Download initiated from manual search:', result.title)
  const toast = useToast()
  toast.success('Download Added', `${result.title} has been sent to your download client`)
  closeManualSearch()
}

async function scanFiles() {
  if (!audiobook.value) return
  scanning.value = true
  try {
    const res = (await apiService.scanAudiobook(audiobook.value.id)) as {
      message: string
      scannedPath?: string
      found: number
      created: number
      audiobook?: AudiobookType
      jobId?: string
    }
    logger.debug('Scan result:', res)
    // If backend enqueued the job it will return 202 Accepted with { jobId }
    if (res?.jobId) {
      scanNotificationsStore.registerManualScan(res.jobId, audiobook.value.id)
      // keep scanning spinner off - queued state shows separately
    }

    // If API returned updated audiobook (blocking fallback), apply it
    if (res?.audiobook) {
      audiobook.value = res.audiobook
    } else if (!scanQueued.value) {
      // If neither queued nor audiobook returned, refresh to pick up any changes
      await loadAudiobook()
    }
  } catch (err) {
    errorTracking.captureException(err as Error, {
      component: 'AudiobookDetailView',
      operation: 'scanFiles',
      metadata: { audiobookId: audiobook.value?.id },
    })
    // Show a non-blocking toast instead of an alert
    const toast = useToast()
    toast.error('Scan failed', err instanceof Error ? err.message : String(err))
  } finally {
    scanning.value = false
  }
}

// Watch library store for updates (SignalR pushes) and refresh audiobook object reactively
watch(
  () => libraryStore.audiobooks,
  () => {
    if (!audiobook.value) return
    const updated = libraryStore.audiobooks.find((b) => b.id === audiobook.value!.id)
    if (updated) {
      // Merge fields to preserve reactivity where possible
      audiobook.value = { ...audiobook.value, ...updated }
    }
  },
  { deep: true },
)

function toggleMonitored() {
  if (audiobook.value) {
    const newMonitoredValue = !audiobook.value.monitored
    audiobook.value = { ...audiobook.value, monitored: newMonitoredValue }

    // Persist to API
    apiService
      .updateAudiobook(audiobook.value.id, { monitored: newMonitoredValue })
      .then(() => {
        logger.debug('Monitored status updated successfully')
      })
      .catch((err) => {
        logger.error('Failed to update monitored status:', err)
        // Revert on error
        if (audiobook.value) {
          audiobook.value = { ...audiobook.value, monitored: !newMonitoredValue }
        }
      })
  }
}

function confirmDelete() {
  resetDeleteOptions()
  showDeleteDialog.value = true
}

function cancelDelete() {
  resetDeleteOptions()
  showDeleteDialog.value = false
}

async function executeDelete() {
  if (!audiobook.value) return

  deleting.value = true
  try {
    const shouldDeleteFolder = deleteFolderOnDisk.value
    const shouldDeleteFiles = deleteFilesOnDisk.value || shouldDeleteFolder
    const success = await libraryStore.removeFromLibrary(audiobook.value.id, {
      deleteFiles: shouldDeleteFiles,
      deleteFolder: shouldDeleteFolder,
      retryAfterBlockedMutation: shouldDeleteFiles
        ? (error) =>
            preparePhysicalDeleteRetry(error, audiobook.value!.id, audiobook.value?.basePath)
        : undefined,
    })
    if (success) {
      const toast = useToast()
      if (shouldDeleteFolder) {
        toast.success('Audiobook deleted', 'The audiobook, its files, and its folder were removed.')
      } else if (shouldDeleteFiles) {
        toast.success('Audiobook deleted', 'The audiobook and its tracked files were removed.')
      } else {
        toast.success('Audiobook deleted', 'The audiobook was removed from the library.')
      }
      // Navigate back to library after successful deletion
      router.push('/books')
    } else if (success === false) {
      const toast = useToast()
      toast.error('Delete failed', libraryStore.error || 'Failed to delete audiobook')
    }
  } catch (err) {
    errorTracking.captureException(err as Error, {
      component: 'AudiobookDetailView',
      operation: 'executeDelete',
      metadata: { audiobookId: audiobook.value?.id },
    })
  } finally {
    deleting.value = false
    resetDeleteOptions()
    showDeleteDialog.value = false
  }
}

function resetDeleteOptions() {
  deleteFilesOnDisk.value = false
  deleteFolderOnDisk.value = false
}

watch(deleteFolderOnDisk, (checked) => {
  if (checked && !deleteFilesOnDisk.value) {
    deleteFilesOnDisk.value = true
  }
})

watch(deleteFilesOnDisk, (checked) => {
  if (!checked && deleteFolderOnDisk.value) {
    deleteFolderOnDisk.value = false
  }
})

function openEditModal() {
  showEditModal.value = true
}

function closeEditModal() {
  showEditModal.value = false
}

async function handleEditSaved() {
  // Refresh the audiobook data after edit
  await loadAudiobook()
}

async function handleOrganizeDone() {
  showOrganizeModal.value = false
  await loadAudiobook()
}

function formatRuntime(minutes: number): string {
  // Guard against legacy data stored in seconds (> 333 hours is unrealistic for minutes)
  const normalized = minutes >= 20000 ? Math.round(minutes / 60) : minutes
  const totalMinutes = Math.floor(normalized)
  const hours = Math.floor(totalMinutes / 60)
  const mins = totalMinutes % 60
  return `${hours}h ${mins}m`
}

function formatFileSize(bytes?: number): string {
  if (!bytes || bytes === 0) return 'Unknown'

  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let size = bytes
  let unitIndex = 0

  while (size >= 1024 && unitIndex < units.length - 1) {
    size /= 1024
    unitIndex++
  }

  return `${size.toFixed(1)} ${units[unitIndex]}`
}

function formatHistoryTime(timestamp: string): string {
  const date = new Date(timestamp)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMins = Math.floor(diffMs / 60000)
  const diffHours = Math.floor(diffMins / 60)
  const diffDays = Math.floor(diffHours / 24)

  if (diffMins < 1) return 'Just now'
  if (diffMins < 60) return `${diffMins} minute${diffMins !== 1 ? 's' : ''} ago`
  if (diffHours < 24) return `${diffHours} hour${diffHours !== 1 ? 's' : ''} ago`
  if (diffDays < 7) return `${diffDays} day${diffDays !== 1 ? 's' : ''} ago`

  return date.toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function getEventIconComponent(eventType: string): Component {
  const icons: Record<string, Component> = {
    Added: PhPlusCircle,
    Downloaded: PhDownload,
    Imported: PhUpload,
    Deleted: PhTrash,
    Updated: PhPencil,
    Monitored: PhBookmark,
    Unmonitored: PhBookmarkSimple,
    Grabbed: PhHandGrabbing,
    Failed: PhWarningCircle,
    'File Added': PhFilePlus,
    'File Removed': PhFileMinus,
  }
  return icons[eventType] || PhCircle
}

function getEventTypeClass(eventType: string): string {
  const classes: Record<string, string> = {
    Added: 'event-success',
    Downloaded: 'event-success',
    Imported: 'event-info',
    Deleted: 'event-danger',
    Updated: 'event-info',
    Monitored: 'event-info',
    Unmonitored: 'event-warning',
    Grabbed: 'event-info',
    Failed: 'event-danger',
    'File Added': 'event-success',
    'File Removed': 'event-warning',
  }
  return classes[eventType] || 'event-default'
}

function formatEventTitle(eventType: string): string {
  const titles: Record<string, string> = {
    Added: 'Added to Library',
    Downloaded: 'Downloaded',
    Imported: 'Imported',
    Deleted: 'Deleted from Library',
    Updated: 'Updated',
    Monitored: 'Monitoring Enabled',
    Unmonitored: 'Monitoring Disabled',
    Grabbed: 'Download Started',
    Failed: 'Failed',
    'File Added': 'File Added',
    'File Removed': 'File Removed',
  }
  return titles[eventType] || eventType
}

function getFileName(filePath?: string): string {
  if (!filePath) return 'Unknown'
  const parts = filePath.split(/[\\/]/)
  const fileName = parts[parts.length - 1]
  return fileName || 'Unknown'
}

function formatDuration(seconds?: number): string {
  if (!seconds || seconds <= 0) return ''
  const sec = Math.floor(seconds)
  const hrs = Math.floor(sec / 3600)
  const mins = Math.floor((sec % 3600) / 60)
  const s = sec % 60
  if (hrs > 0) return `${hrs}h ${mins}m ${s}s`
  if (mins > 0) return `${mins}m ${s}s`
  return `${s}s`
}

function isFileAccordionExpanded(fileId: number): boolean {
  return expandedFileAccordions.value.has(fileId)
}

function toggleFileAccordion(fileId: number): void {
  if (expandedFileAccordions.value.has(fileId)) {
    expandedFileAccordions.value.delete(fileId)
  } else {
    expandedFileAccordions.value.add(fileId)
  }
}

function getFullPath(relativePath?: string): string {
  if (!relativePath) return 'Unknown'

  const basePath = audiobook.value?.basePath
  const pathKind = detectPathKind(basePath)
  const isAbsolute = isAbsolutePath(relativePath, pathKind)
  if (isAbsolute) return relativePath
  if (!basePath) return relativePath
  return joinPaths(basePath, relativePath, pathKind)
}

function formatDate(dateString?: string): string {
  if (!dateString) return 'Unknown'
  // If the string already includes a timezone (Z or ±HH:MM), parse as-is
  const hasTimezone = /[zZ]|[+-]\d{2}:?\d{2}$/.test(dateString)
  const date = new Date(hasTimezone ? dateString : `${dateString}Z`)
  if (Number.isNaN(date.getTime())) return 'Unknown'
  return date.toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    timeZone: 'UTC',
  })
}
</script>

<style scoped>
.audiobook-detail {
  --detail-top-nav-height: 60px;
  min-height: 100vh;
  background-color: #1a1a1a;
  padding-top: var(--detail-top-nav-height);
  /* Add padding to account for fixed local nav */
}

.top-nav {
  position: fixed;
  top: var(--app-top-offset, 60px);
  /* Account for global header nav + optional warning banner */
  left: 200px;
  /* Account for sidebar width */
  right: 0;
  z-index: 99;
  /* Below global nav (1000) but above content */
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 20px;
  background-color: #2a2a2a;
  border-bottom: 1px solid #333;
}

@media (max-width: 768px) {
  .top-nav {
    left: 0;
    /* Full width on mobile */
  }
}

.nav-actions {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}

/* Desktop alignment tweaks: ensure primary and secondary actions line up and align to the right */
@media (min-width: 769px) {
  .nav-actions {
    align-items: center;
    display: flex;
    gap: 8px;
    flex-wrap: nowrap;
  }

  .primary-actions {
    display: flex;
    gap: 8px;
    align-items: center;
  }

  .secondary-actions {
    display: flex;
    gap: 12px;
    align-items: center;
  }

  .more-wrapper {
    display: inline-flex;
    align-items: center;
  }
}

/* Desktop: tighter, consistent sizing and ordering for nav buttons */
@media (min-width: 769px) {
  .top-nav {
    padding: 12px 20px;
  }

  /* Ensure nav-actions stays on the right and items don't wrap */
  .nav-actions {
    margin-left: auto;
    display: flex;
    gap: 8px;
    align-items: center;
    flex-wrap: nowrap;
  }

  /* Make each button a uniform height and inline-flex for better baseline alignment */
  .nav-actions .nav-btn {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    height: 36px;
    padding: 8px 12px;
    white-space: nowrap;
  }

  /* Icon-only nav buttons (use .icon-button) */
  .nav-actions .nav-btn.icon-button {
    padding: 0;
    width: 36px;
    height: 36px;
    gap: 0;
    justify-content: center;
  }

  /* Remove extra margins */
  .primary-actions {
    margin-right: 0;
  }

  .secondary-actions {
    margin-left: 0;
  }

  /* Make delete button always appear last and slightly emphasized */
  .secondary-actions .delete-btn {
    order: 99;
    padding-left: 10px;
    padding-right: 10px;
  }
}

.nav-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 12px;
  background-color: #3a3a3a;
  border: 1px solid #555;
  border-radius: 6px;
  color: #fff;
  font-size: 13px;
  cursor: pointer;
  transition: background-color 0.2s;
}

.nav-btn:hover {
  background-color: #4a4a4a;
}

.nav-btn.delete-btn {
  background-color: #e74c3c;
  border-color: #c0392b;
}

.nav-btn.delete-btn:hover {
  background-color: #c0392b;
}

.nav-btn.debug-btn {
  background-color: #5865f2;
  border-color: #4752c4;
}

.nav-btn.debug-btn:hover {
  background-color: #4752c4;
}

/* Test Menu Styles */
.test-menu-container {
  position: relative;
}

.test-menu-btn {
  background-color: #5865f2;
  border-color: #4752c4;
}

.test-menu-btn:hover {
  background-color: #4752c4;
}

.test-dropdown {
  position: absolute;
  top: 100%;
  right: 0;
  margin-top: 4px;
  background-color: #2a2a2a;
  border: 1px solid #555;
  border-radius: 6px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
  min-width: 180px;
  z-index: 100;
}

/* More dropdown (mobile) should be absolutely positioned so it doesn't expand the top-nav */
.more-wrapper {
  position: relative;
}

.more-dropdown {
  position: absolute;
  top: calc(100% + 6px);
  right: 0;
  margin-top: 4px;
  background-color: #2a2a2a;
  border: 1px solid #555;
  border-radius: 6px;
  box-shadow: 0 6px 18px rgba(0, 0, 0, 0.35);
  min-width: 200px;
  z-index: 1100;
  display: flex;
  flex-direction: column;
}

.more-dropdown .dropdown-item {
  border-radius: 6px;
}

.dropdown-item {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 10px 14px;
  background: none;
  border: none;
  color: #fff;
  font-size: 13px;
  cursor: pointer;
  transition: background-color 0.2s;
  text-align: left;
}

.dropdown-item:first-child {
  border-radius: 6px;
}

.dropdown-item:last-child {
  border-radius: 6px;
}

.dropdown-item:hover:not(:disabled) {
  background-color: #3a3a3a;
}

.dropdown-item:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* Webhook Selector Modal */
.webhook-selector-modal {
  max-width: 500px;
}

.modal-description {
  margin-bottom: 16px;
  color: #aaa;
  font-size: 14px;
}

.webhook-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.webhook-item {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  padding: 14px 16px;
  background-color: #2a2a2a;
  border: 1px solid #555;
  border-radius: 6px;
  color: #fff;
  font-size: 14px;
  cursor: pointer;
  transition: all 0.2s;
  text-align: left;
}

.webhook-item:hover:not(:disabled) {
  background-color: #3a3a3a;
  border-color: #5865f2;
  transform: translateX(4px);
}

.webhook-item:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.webhook-name {
  flex: 1;
  font-weight: 500;
}

.hero-section {
  position: relative;
  padding: 40px 40px;
  overflow: hidden;
}

@media (max-width: 768px) {
  .hero-section {
    padding: 40px 20px;
  }
}

.backdrop {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-size: cover;
  background-position: center;
  filter: blur(20px) brightness(0.3);
  transform: scale(1.1);
}

.hero-content {
  position: relative;
  display: flex;
  gap: 40px;
  max-width: 1600px;
  margin: 0 auto;
  z-index: 1;
}

@media (min-width: 1200px) {
  .hero-content {
    gap: 40px;
  }
}

@media (max-width: 768px) {
  .hero-content {
    flex-direction: column;
    gap: 20px;
  }
}

.poster-container {
  flex-shrink: 0;
}

@media (max-width: 768px) {
  .poster-container {
    margin: 0 auto;
  }
}

.poster {
  width: 350px;
  height: 350px;
  object-fit: cover;
  border-radius: 6px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.6);
}

@media (max-width: 768px) {
  .poster {
    width: 250px;
    height: 250px;
  }
}

.info-section {
  flex: 1;
  color: #fff;
  min-width: 0;
}

/* The metadata refresh rides at the right end of the title's own line */
.hero-title-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.hero-actions {
  flex-shrink: 0;
  display: flex;
  gap: 8px;
}

.hero-refresh-btn {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  gap: 8px;
  margin-top: 8px;
  padding: 8px 12px;
  border-radius: 6px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  background: rgba(0, 0, 0, 0.35);
  color: #e6eef8;
  font-size: 12px;
  cursor: pointer;
  transition:
    background-color 0.15s,
    border-color 0.15s;
}

.hero-refresh-btn:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.12);
  border-color: rgba(255, 255, 255, 0.2);
}

.hero-refresh-btn:disabled {
  opacity: 0.6;
  cursor: default;
}

.hero-refresh-btn:focus-visible {
  outline: 3px solid rgba(33, 150, 243, 0.18);
  outline-offset: 2px;
}

@media (max-width: 768px) {
  .hero-title-row {
    flex-direction: column;
    align-items: flex-start;
    gap: 8px;
  }

  .hero-refresh-btn {
    margin-top: 0;
  }
}

.title {
  font-size: 3rem;
  font-weight: 500;
  margin: 0 0 12px 0;
  color: #fff;
  line-height: 1.2;
}

@media (max-width: 768px) {
  .title {
    font-size: 2rem;
    text-align: center;
  }
}

.subtitle {
  font-size: 1.4rem;
  color: #ccc;
  margin-bottom: 20px;
}

/* Sits under the title, below the subtitle when there is one worth keeping */
.hero-series {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  margin-bottom: 20px;
}

/* Tighten the gap when the two stack, so they read as one block */
.subtitle + .hero-series {
  margin-top: -10px;
}

@media (max-width: 768px) {
  .subtitle {
    font-size: 1rem;
    text-align: center;
  }

  .hero-series {
    justify-content: center;
  }
}

.meta-info {
  display: flex;
  align-items: center;
  gap: 20px;
  margin-bottom: 24px;
  font-size: 15px;
  color: #ccc;
  flex-wrap: wrap;
}

.meta-info span {
  display: flex;
  align-items: center;
  gap: 4px;
}

/*
 * Authors read as one sentence, so the list and its separators flow inline rather than
 * becoming flex items of the surrounding meta row.
 */
.meta-info .meta-author-list,
.meta-info .meta-author-sep {
  display: inline;
}

/* Each author opens its collection, like the author tags further down the page */
.meta-author-link {
  background: none;
  border: none;
  padding: 0;
  font: inherit;
  color: inherit;
  cursor: pointer;
}

.meta-author-link:hover,
.meta-author-link:focus-visible {
  color: var(--brand-500);
  text-decoration: underline;
}

.runtime i,
.rating i {
  color: var(--brand-500);
}

.rating-count,
.rating-split {
  color: #aaa;
  font-size: 13px;
}

.rating-split {
  margin-left: 6px;
}

@media (max-width: 768px) {
  .meta-info {
    justify-content: center;
  }
}

.file-path {
  padding: 2px 6px;
  border-radius: 6px;
  font-size: 13px;
  color: #aaa;
}

.key-details {
  display: flex;
  flex-wrap: wrap;
  /* Stretch, so every box in the row ends up the height of the tallest */
  align-items: stretch;
  gap: 12px;
  margin-bottom: 20px;
}

.detail-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  background-color: rgba(255, 255, 255, 0.05);
  border-radius: 6px;
  font-size: 14px;
  /* Let a long path ellipsis rather than push the row onto a second line */
  min-width: 0;
}

/*
 * The pills share this row with the detail boxes, so they take the same metrics and
 * the row sits level. Keep in step with .detail-item above.
 */
.key-details .pill {
  gap: 10px;
  padding: 10px 14px;
  font-size: 14px;
}

.detail-item span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/*
 * The preview button shares this row with the detail boxes and the pills, so it takes
 * their metrics too. Keep in step with .detail-item above.
 */
.hero-preview :deep(.btn-preview.has-label) {
  padding: 10px 14px;
  border-radius: 6px;
  border-color: rgba(255, 255, 255, 0.12);
  background-color: rgba(255, 255, 255, 0.05);
  font-size: 14px;
  gap: 10px;
}

/* Open, it takes the width the seek bar needs rather than the whole row, so the pills
   beside it keep their line. */
.hero-preview.preview-open {
  flex: 0 1 24rem;
  align-items: center;
}

.detail-item i {
  color: var(--brand-500);
}

.description {
  color: #ccc;
  line-height: 1.6;
  max-width: 900px;
  position: relative;
}

.description-content {
  white-space: pre-wrap;
}

/* The toggle is the tail of the sentence it hides, not a control beside it */
.show-more-btn {
  margin-left: 0.35em;
  padding: 0;
  border: none;
  background: none;
  font: inherit;
  font-weight: 700;
  color: inherit;
  text-decoration: underline;
  cursor: pointer;
}

.show-more-btn:hover,
.show-more-btn:focus-visible {
  color: #fff;
}

.description :deep(p) {
  margin: 0 0 12px 0;
}

.description :deep(br) {
  display: block;
  margin: 8px 0;
}

.description :deep(strong),
.description :deep(b) {
  color: #fff;
  font-weight: 500;
}

.description :deep(em),
.description :deep(i) {
  font-style: italic;
}

.description :deep(a) {
  color: var(--brand-500);
}

.description :deep(a:hover) {
  text-decoration: underline;
}

.description :deep(ul),
.description :deep(ol) {
  margin: 12px 0;
  padding-left: 24px;
}

.description :deep(li) {
  margin: 4px 0;
}

.tabs-container {
  background-color: #2a2a2a;
  border-bottom: 1px solid #333;
  padding: 0 40px;
}

@media (max-width: 768px) {
  .tabs-container {
    padding: 0 20px;
  }
}

/* Show mobile select and hide desktop tabs where appropriate */
.tabs-mobile {
  display: none;
}

.tabs-desktop {
  display: block;
}

@media (max-width: 768px) {
  .tabs-mobile {
    display: block;
  }

  .tab-dropdown {
    width: 100%;
  }

  .tabs-desktop {
    display: none;
  }

  /* Make top nav buttons wrap and be touch friendly on small screens */
  .top-nav {
    padding: 10px 12px;
    right: 0;
  }

  .nav-actions {
    flex-wrap: wrap;
    gap: 6px;
  }

  .nav-btn {
    padding: 8px 10px;
    min-width: 44px;
  }
}

/* Improved mobile layout for nav actions: keep nav and actions inline on mobile */
@media (max-width: 768px) {
  .top-nav {
    display: flex;
    flex-direction: row;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    padding: 10px 12px;
  }

  /* Keep the back button prominent but inline with actions on mobile */
  .top-nav > .nav-btn:first-of-type {
    width: auto;
    justify-content: flex-start;
    gap: 10px;
    padding: 10px 12px;
    font-weight: 500;
    min-width: 0;
  }

  /* On mobile hide the primary-actions container (we surface primary actions inside the More menu) */
  .primary-actions {
    display: none;
  }

  /* Make nav-actions size to content so they stay inline with the back button */
  .nav-actions {
    display: grid;
    grid-auto-flow: column;
    grid-auto-columns: auto;
    gap: 8px;
    width: auto;
    align-items: center;
  }

  @media (max-width: 480px) {
    .nav-actions {
      grid-auto-columns: auto;
    }
  }

  .nav-actions .nav-btn {
    width: auto;
    justify-content: center;
    padding: 10px 8px;
    font-size: 14px;
    border-radius: 6px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  /* Make icon slightly larger to improve affordance */
  .nav-actions .nav-btn svg,
  .top-nav > .nav-btn svg {
    width: 20px;
    height: 20px;
  }

  /* Reduce visual noise for disabled buttons and keep them tappable */
  .nav-actions .nav-btn:disabled {
    opacity: 0.6;
    cursor: not-allowed;
  }
}

.tabs {
  display: flex;
  gap: 4px;
  max-width: 1600px;
  margin: 0 auto;
}

.tab {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 12px 20px;
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  color: #999;
  cursor: pointer;
  transition: all 0.2s;
  font-size: 14px;
}

.tab:hover {
  color: #fff;
}

.tab-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 18px;
  height: 18px;
  padding: 0 5px;
  border-radius: 999px;
  font-size: 11px;
  font-weight: 600;
  line-height: 1;
}

.tab-badge--issue {
  background: rgba(231, 76, 60, 0.2);
  color: #e74c3c;
}

.tab-badge--note {
  background: rgba(243, 156, 18, 0.18);
  color: #f39c12;
}

.tab.active {
  color: var(--brand-500);
  border-bottom-color: var(--brand-500);
}

.tab-content {
  padding: 40px 40px;
  max-width: 1600px;
  margin: 0 auto;
}

@media (max-width: 768px) {
  .tab-content {
    padding: 30px 20px;
  }
}

.details-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(350px, 1fr));
  gap: 24px;
}

@media (min-width: 1200px) {
  .details-grid {
    grid-template-columns: repeat(3, 1fr);
  }
}

@media (max-width: 768px) {
  .details-grid {
    grid-template-columns: 1fr;
  }
}

.detail-card {
  background-color: #2a2a2a;
  border: 1px solid #333;
  border-radius: 6px;
  padding: 20px;
}

.detail-card h3 {
  margin: 0 0 16px 0;
  color: #fff;
  font-size: 16px;
  border-bottom: 1px solid #333;
  padding-bottom: 12px;
}

.detail-row {
  display: flex;
  justify-content: space-between;
  padding: 8px 0;
  border-bottom: 1px solid #333;
}

.detail-row:last-child {
  border-bottom: none;
}

.detail-row .label {
  color: #999;
  font-size: 14px;
}

.detail-row .value {
  color: #fff;
  font-size: 14px;
  text-align: right;
}

.detail-link-tags {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
}

.detail-series-memberships {
  gap: 10px;
}

.detail-series-membership {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.detail-series-number {
  font-size: 12px;
  color: var(--text-secondary);
}

.detail-link-tag {
  appearance: none;
  font-size: 12px;
  cursor: pointer;
  line-height: 1.2;
}

.detail-link-tag:hover {
  background: rgba(var(--brand-rgb), 0.2);
  border-color: rgba(var(--brand-rgb), 0.52);
  transform: translateY(-1px);
}

.detail-link-tag:focus-visible {
  outline: 2px solid rgba(var(--brand-rgb), 0.5);
  outline-offset: 2px;
}

.detail-row-stacked {
  align-items: flex-start;
  gap: 12px;
}

.detail-row-stacked .label {
  padding-top: 4px;
}

.detail-row-stacked .value {
  text-align: right;
}

.identifiers-list {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 8px;
  max-width: 70%;
}

.identifier-item {
  display: inline-flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  flex-wrap: wrap;
}

.identifier-type {
  color: #b3b3b3;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.02em;
  text-transform: uppercase;
}

.identifier-link {
  color: #fff;
  font-size: 14px;
  word-break: break-word;
}

a.identifier-link:hover {
  color: var(--brand-300);
}

.identifier-badge {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 11px;
  font-weight: 600;
}

.identifier-badge.primary {
  background: rgba(59, 130, 246, 0.16);
  border: 1px solid rgba(59, 130, 246, 0.45);
  color: #bfdbfe;
}

.genre-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.genre-tag {
  appearance: none;
  cursor: pointer;
  padding: 6px 12px;
  background-color: #3a3a3a;
  border: 1px solid #555;
  border-radius: 6px;
  color: #fff;
  font-size: 12px;
}

.detail-genre-tag:hover {
  background-color: #404040;
  border-color: var(--brand-500);
  color: #fff;
}

.tags-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.tag-badge {
  display: inline-flex;
  align-items: center;
  padding: 6px 12px;
  background-color: #2a2a2a;
  border: 1px solid #3a3a3a;
  border-radius: 6px;
  color: #e0e0e0;
  font-size: 12px;
  font-weight: 500;
  transition: all 0.2s ease;
}

.tag-badge:hover {
  background-color: #333;
  border-color: var(--brand-500);
  color: white;
}

.files-content,
.chapters-content,
.credits-content,
.tags-content,
.history-content {
  background-color: #2a2a2a;
  border: 1px solid #333;
  border-radius: 6px;
  padding: 20px;
}

.files-header,
.history-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
  padding-bottom: 12px;
  border-bottom: 1px solid #333;
}

.files-header h3,
.history-header h3 {
  margin: 0;
  color: #fff;
}

.action-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 12px;
  background-color: #3a3a3a;
  border: 1px solid #555;
  border-radius: 6px;
  color: #fff;
  font-size: 13px;
  cursor: pointer;
  transition: background-color 0.2s;
}

.action-btn:hover {
  background-color: #4a4a4a;
}

.file-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.file-item {
  display: flex;
  flex-direction: column;
  padding: 12px;
  background-color: #333;
  border-radius: 6px;
  transition: all 0.2s ease;
}

.file-item.expanded {
  background-color: #3a3a3a;
}

.file-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  cursor: pointer;
  width: 100%;
}

.file-info {
  display: flex;
  align-items: center;
  gap: 12px;
  color: #fff;
  flex: 1;
  /* The open player takes the line, so the name wraps beneath it rather than being
     squeezed out of the row. */
  flex-wrap: wrap;
}

.file-info i {
  font-size: 24px;
  color: var(--brand-500);
}

.file-name {
  font-weight: 500;
}

.file-meta {
  color: #999;
}

.file-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}

.file-chapter-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 8px;
  border-radius: 6px;
  background-color: rgba(231, 76, 60, 0.12);
  border: 1px solid rgba(231, 76, 60, 0.18);
  color: #e74c3c;
  font-size: 11px;
  font-weight: 500;
  white-space: nowrap;
}

.audio-audit {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  margin-bottom: 12px;
  padding: 10px 14px;
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.06);
  background: rgba(255, 255, 255, 0.03);
}

.chapters-content {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.audio-audit--summary {
  margin-bottom: 0;
}

.credits-content {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.credits-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 2rem;
  color: var(--text-secondary);
}

.audio-audit-when {
  margin-top: 4px;
  font-size: 12px;
  color: var(--text-muted);
}

.credits-compare {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 14px;
}

.credits-col,
.credits-recommend,
.credits-transcript {
  padding: 12px 14px;
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.02);
}

.credits-col h4,
.credits-recommend h4,
.credits-transcript h4 {
  margin: 0 0 8px;
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-muted);
}

.credits-transcript p + h4 {
  margin-top: 12px;
}

.credits-col dl {
  margin: 0;
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 4px 12px;
  font-size: 13px;
}

.credits-col dt {
  color: var(--text-muted);
}

.credits-col dd {
  margin: 0;
}

.credits-missing {
  color: var(--text-muted);
  font-style: italic;
}

.credits-recommend ul {
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.credits-recommend li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-size: 13px;
}

.credits-text {
  margin: 0;
  font-size: 13px;
  line-height: 1.6;
  color: var(--text-secondary);
}

@media (max-width: 800px) {
  .credits-compare {
    grid-template-columns: 1fr;
  }
}

.edition-check {
  margin-top: 16px;
  padding-top: 12px;
  border-top: 1px solid var(--border-color, rgba(128, 128, 128, 0.25));
}

.edition-check h4 {
  margin: 0 0 4px;
}

.edition-explain {
  margin: 0 0 8px;
  color: var(--text-secondary);
  font-size: 0.85em;
}

.edition-check .file-repair-btn {
  margin-top: 8px;
}

.audio-audit--mismatch,
.audio-audit--narrator-mismatch,
.audio-audit--incomplete {
  border-color: rgba(231, 76, 60, 0.35);
  background: rgba(231, 76, 60, 0.08);
}

.audio-audit--match {
  border-color: rgba(46, 204, 113, 0.25);
}

/* Settled rather than alarming: the verdict still reads, but somebody has answered it. */
.audio-audit--accepted {
  border-color: rgba(46, 204, 113, 0.25);
  background: rgba(46, 204, 113, 0.05);
}

.audio-audit--accepted .audio-audit-icon {
  color: #2ecc71;
}

.audio-audit--fixable {
  border-color: rgba(243, 156, 18, 0.4);
  background: rgba(243, 156, 18, 0.08);
}

.audio-audit--fixable .audio-audit-icon {
  color: #f39c12;
}

.audio-audit-icon {
  flex-shrink: 0;
  margin-top: 2px;
  width: 18px;
  height: 18px;
  color: var(--text-muted);
}

.audio-audit--mismatch .audio-audit-icon,
.audio-audit--narrator-mismatch .audio-audit-icon,
.audio-audit--incomplete .audio-audit-icon {
  color: #e74c3c;
}

.audio-audit--match .audio-audit-icon {
  color: #2ecc71;
}

.audio-audit-body {
  flex: 1;
  min-width: 0;
}

.audio-audit-verdict {
  font-weight: 600;
  font-size: 14px;
}

.audio-audit-reason {
  color: var(--text-secondary);
  font-size: 13px;
  margin-top: 2px;
}

.audio-audit-heard {
  margin-top: 6px;
  color: var(--text-muted);
  font-size: 12px;
  font-style: italic;
}

.audio-audit-actions {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
}

.file-repair-btn--quiet {
  border-color: rgba(255, 255, 255, 0.15);
  color: var(--text-secondary);
}

.file-chapter-badge--note {
  background-color: rgba(148, 163, 184, 0.15);
  border-color: rgba(148, 163, 184, 0.35);
  color: var(--text-muted);
}

.file-item--not-found .file-name {
  opacity: 0.6;
  text-decoration: line-through;
}

.file-repair-btn--danger {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  border-color: #e74c3c;
  color: #e74c3c;
}

.file-repair-btn {
  padding: 3px 10px;
  border: 1px solid var(--brand-500);
  border-radius: 6px;
  background: transparent;
  color: var(--brand-500);
  font-size: 12px;
  cursor: pointer;
}

.file-repair-btn:disabled {
  opacity: 0.5;
  cursor: default;
}

.accordion-toggle {
  color: #999;
  transition: transform 0.2s ease;
  font-size: 16px;
}

.accordion-toggle.rotated {
  transform: rotate(180deg);
}

.file-accordion {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px solid #444;
  animation: slideDown 0.2s ease-out;
}

@keyframes slideDown {
  from {
    opacity: 0;
    max-height: 0;
  }

  to {
    opacity: 1;
    max-height: 500px;
  }
}

.metadata-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 14px;
}

.metadata-table tbody tr {
  border-bottom: 1px solid #444;
}

.metadata-table tbody tr:last-child {
  border-bottom: none;
}

.metadata-label {
  color: #999;
  padding: 8px 12px 8px 0;
  font-weight: 500;
  width: 120px;
  vertical-align: top;
}

.metadata-value {
  color: #fff;
  padding: 8px 0;
  word-break: break-word;
}

.file-info {
  display: flex;
  align-items: center;
  gap: 12px;
  color: #fff;
}

.file-info i {
  font-size: 24px;
  color: var(--brand-500);
}

.file-size {
  color: #999;
  font-size: 14px;
}

.empty-history {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 60px 20px;
  color: #666;
}

.empty-history i {
  font-size: 48px;
  margin-bottom: 12px;
}

.empty-history .hint {
  font-size: 14px;
  color: #555;
  margin-top: 8px;
}

.empty-files {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 60px 20px;
  color: #666;
}

.empty-files i {
  font-size: 48px;
  margin-bottom: 12px;
}

.empty-files .hint {
  font-size: 14px;
  color: #555;
  margin-top: 8px;
}

/* Mobile-specific refinements to improve layout and prevent overflow */
@media (max-width: 768px) {
  /* Make poster a bit smaller and centered for narrow viewports */
  .poster {
    width: 200px;
    height: 200px;
    margin: 0 auto;
    display: block;
  }

  .hero-content {
    align-items: flex-start;
  }

  .info-section {
    padding: 0 8px;
  }

  /* Allow long titles and metadata to wrap instead of causing horizontal scroll */
  .title {
    word-break: break-word;
    overflow-wrap: anywhere;
  }

  .detail-item span {
    white-space: normal;
    overflow-wrap: anywhere;
    min-width: 0;
  }

  /* Stack metadata table rows on small screens so the table doesn't overflow */
  .metadata-table tbody tr {
    display: block;
    padding: 8px 0;
    border-bottom: 1px solid #444;
  }

  .metadata-label {
    display: block;
    width: auto;
    padding-bottom: 6px;
  }

  .metadata-value {
    display: block;
    padding-bottom: 12px;
    word-break: break-word;
  }

  /* Make file lists and tab content reserve space for scrollbars to avoid layout shifts */
  .file-list,
  .tab-content,
  .search-results-inline {
    scrollbar-gutter: stable;
  }

  /* Ensure dropdowns and test menus sit above the fixed top-nav */
  .test-dropdown,
  .test-menu-container .test-dropdown,
  .test-menu-container .test-dropdown .dropdown-item {
    z-index: 1200;
  }

  /* Tweak top nav spacing for very small screens */
  .nav-actions {
    gap: 8px;
  }

  .nav-btn {
    min-width: 0;
    padding: 8px 10px;
    font-size: 13px;
  }
}

@media (max-width: 480px) {
  .poster {
    width: 160px;
    height: 160px;
  }

  .title {
    font-size: 1.4rem;
  }

  .nav-btn {
    padding: 8px 10px;
    font-size: 12px;
  }
}

/* History Styles */
.history-loading,
.history-error {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 40px 20px;
  color: #999;
}

.history-loading i {
  font-size: 36px;
  margin-bottom: 12px;
}

.history-error i {
  font-size: 36px;
  margin-bottom: 12px;
  color: #e74c3c;
}

.retry-btn,
.refresh-btn {
  margin-top: 12px;
  padding: 8px 16px;
  background-color: var(--brand-500);
  border: none;
  border-radius: 6px;
  color: #fff;
  cursor: pointer;
  font-size: 14px;
  transition: background-color 0.2s;
}

.retry-btn:hover,
.refresh-btn:hover {
  background-color: #005fa3;
}

.refresh-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.history-list {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.history-entry {
  display: flex;
  gap: 16px;
  padding: 16px;
  background-color: #333;
  border-radius: 6px;
  border-left: 3px solid #555;
  transition:
    transform 0.2s,
    box-shadow 0.2s;
}

.history-entry:hover {
  transform: translateX(4px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
}

.history-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: 50%;
  flex-shrink: 0;
}

.history-icon i {
  font-size: 20px;
}

.event-success {
  background-color: rgba(46, 204, 113, 0.2);
  color: #2ecc71;
}

.event-info {
  background-color: rgba(52, 152, 219, 0.2);
  color: #3498db;
}

.event-warning {
  background-color: rgba(241, 196, 15, 0.2);
  color: #f1c40f;
}

.event-danger {
  background-color: rgba(231, 76, 60, 0.2);
  color: #e74c3c;
}

.event-default {
  background-color: rgba(149, 165, 166, 0.2);
  color: #95a5a6;
}

.history-details {
  flex: 1;
}

.history-event {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
}

.event-type {
  font-weight: 500;
  color: #fff;
  font-size: 14px;
}

.discord-pill {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 11px;
  color: #5865f2;
  background-color: rgba(88, 101, 242, 0.15);
  padding: 2px 8px;
  border-radius: 6px;
  border: 1px solid rgba(88, 101, 242, 0.3);
  font-weight: 500;
}

.event-source {
  font-size: 12px;
  color: #999;
  padding: 2px 8px;
  background-color: rgba(255, 255, 255, 0.05);
  border-radius: 6px;
}

.history-message {
  color: #ccc;
  font-size: 14px;
  margin-bottom: 8px;
  line-height: 1.4;
}

.history-time {
  color: #777;
  font-size: 12px;
}

.loading-container,
.error-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  color: #ccc;
  background-color: #1a1a1a;
}

.loading-container i,
.error-container i {
  font-size: 48px;
  margin-bottom: 16px;
}

.loading-container i {
  color: var(--brand-500);
}

.error-container i {
  color: #e74c3c;
}

.error-container h2 {
  color: #fff;
  margin: 0 0 8px 0;
}

.back-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 20px;
  padding: 12px 24px;
  background-color: var(--brand-500);
  border: none;
  border-radius: 6px;
  color: #fff;
  cursor: pointer;
  font-size: 14px;
  transition: background-color 0.2s;
}

.back-btn:hover {
  background-color: #005fa3;
}

/* Delete dialog styling is centralized in `src/assets/modals.css` */
/* Legacy .dialog classes are still used in a few places (e.g., Audiobook detail delete), but visual styles are now centralized. */
.delete-options {
  margin-top: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.delete-options .checkbox-row {
  margin-top: 0;
}

.delete-options .checkbox-label {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
  text-align: left;
  padding: 0.9rem 1rem;
  border-radius: 12px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  background: rgba(255, 255, 255, 0.03);
  transition:
    border-color 0.2s ease,
    background-color 0.2s ease;
}

.delete-options .checkbox-label:hover {
  border-color: rgba(var(--brand-rgb), 0.35);
  background: rgba(255, 255, 255, 0.05);
}

.delete-options .checkbox-input {
  margin-top: 2px;
  accent-color: var(--brand-500);
}

.delete-options .checkbox-content {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.delete-options .checkbox-title {
  color: #f5f7fa;
  font-weight: 600;
}

.delete-options .checkbox-content small {
  color: #b9c0c8;
  line-height: 1.4;
}

/* Ensure visible spacing between secondary action buttons across breakpoints */
.secondary-actions {
  display: flex;
  gap: 0.5rem;
}

/* Keep delete button padding consistent */
.secondary-actions .delete-btn {
  padding-left: 10px;
  padding-right: 10px;
}
</style>

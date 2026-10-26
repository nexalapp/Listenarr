using System.ComponentModel.DataAnnotations.Schema;
using Listenarr.Domain.Common;

namespace Listenarr.Domain.Configuration
{
    public class ApplicationSettings
    {
        public int Version { get; set; }
        public int Id { get; set; } = 1; // Singleton pattern - only one settings record
        public string OutputPath { get; set; } = string.Empty;

        // Folder naming pattern (base directory structure)
        // Available variables:
        // {Author} - Audiobook author
        // {Narrator} - Narrator name(s)
        // {Series} - Series name (if applicable)
        // {SeriesNumber} - Position in series (e.g., "1", "2")
        // {Title} - Book/audiobook title
        // {Subtitle} - Book subtitle
        // {Edition} - User-defined edition label
        // {Publisher} - Publisher name
        // {Language} - Metadata language
        // {Asin} - Audible ASIN
        // {Year} - Publication year
        public string FolderNamingPattern { get; set; } = "{Author}/{Series}/{Title}";

        // File naming pattern for SINGLE-FILE imports (one audio file per audiobook)
        // Available variables:
        // {Author} - Audiobook author
        // {Narrator} - Narrator name(s)
        // {Series} - Series name (if applicable)
        // {SeriesNumber} - Position in series (e.g., "1", "2")
        // {Title} - Book/audiobook title
        // {Subtitle} - Book subtitle
        // {Edition} - User-defined edition label
        // {Publisher} - Publisher name
        // {Language} - Metadata language
        // {Asin} - Audible ASIN
        // {Year} - Publication year
        // {Quality} - Audio quality (e.g., "64kbps mp3")
        public string FileNamingPattern { get; set; } = "{Title}";

        // File naming pattern for MULTI-FILE imports (multiple audio files per audiobook)
        // Use {DiskNumber} or {DiskNumber:00}, {ChapterNumber} or {ChapterNumber:00} to differentiate files
        // Available variables:
        // {Author} - Audiobook author
        // {Narrator} - Narrator name(s)
        // {Series} - Series name (if applicable)
        // {SeriesNumber} - Position in series (e.g., "1", "2")
        // {Title} - Book/audiobook title
        // {Subtitle} - Book subtitle
        // {Edition} - User-defined edition label
        // {Publisher} - Publisher name
        // {Language} - Metadata language
        // {Asin} - Audible ASIN
        // {DiskNumber} or {DiskNumber:00} - Disk/part number (00 = zero-padded)
        // {ChapterNumber} or {ChapterNumber:00} - Chapter number (00 = zero-padded)
        // {Year} - Publication year
        // {Quality} - Audio quality (e.g., "64kbps mp3")
        public string MultiFileNamingPattern { get; set; } = "{Title}-{DiskNumber:00}-{ChapterNumber:00}";

        public bool EnableMetadataProcessing { get; set; } = true;
        public bool EnableCoverArtDownload { get; set; } = true;
        public string AudnexusApiUrl { get; set; } = "https://api.audnex.us";
        public int MaxConcurrentDownloads { get; set; } = 3;
        public int PollingIntervalSeconds { get; set; } = 30;
        public bool EnableNotifications { get; set; } = false;
        public List<string> AllowedFileExtensions
        {
            get
            {
                return [.. FileUtils.NormalizeExtensions(field)];
            }
            set;
        } = [".mp3", ".flac", ".m4a", ".m4b", ".ogg"];

        // Number of seconds a download must be observed in the client as "complete" before
        // the system will finalize it (stability window). Keeping a short default (10s)
        // avoids accidental long delays while still allowing this to be tuned by admins.
        public int DownloadCompletionStabilitySeconds { get; set; } = 10;

        // Retry/backoff settings for when a finalized download has no discoverable source file
        // at the time of finalization. These control how the monitor schedules retries when
        // files are still being extracted/moved by the client.
        public int MissingSourceRetryInitialDelaySeconds { get; set; } = 30;
        public int MissingSourceMaxRetries { get; set; } = 3;

        // Action to take when a download completes
        public FileAction CompletedFileAction { get; set; } = FileAction.Copy;

        // Whether to extract archive files (zip/rar/7z) when discovered in a completed download
        public bool ExtractArchives { get; set; } = true;

        // Whether an imported MP3 audiobook is automatically queued for conversion to a
        // single chaptered M4B. Off until the conversion has been proven against the real
        // library: it is slow, IO-heavy, and rewrites what the library serves.
        public bool ConvertMp3ToM4b { get; set; } = false;

        // What happens to the source MP3s once a conversion has been verified.
        public ConversionSourceDisposition ConversionSourceDisposition { get; set; } =
            ConversionSourceDisposition.Archive;

        // Where archived source MP3s are moved to. Empty means the conversion will not
        // archive, because moving files to an unconfigured location is not a safe guess.
        public string ConversionArchivePath { get; set; } = string.Empty;

        // Whether a book is automatically queued for tag writing once its M4B lands in
        // the library, from a download import or from a conversion. Off until the
        // mapping has been checked against the real library: it rewrites files.
        public bool WriteMetadataTags { get; set; } = false;

        // Whether tag writing embeds the book's cover art when the file carries none.
        // Replacing existing art is never automatic; this only fills a gap.
        public bool EmbedCoverArtInTags { get; set; } = true;

        // Whether the app may listen to audio. Transcription is what lets a chapter
        // repair find the author's chapters inside a CD-rip's tracks and put names on
        // placeholder titles: it hears "Chapter Four" at a mark and keeps it. Off by
        // default because it costs CPU minutes per book and downloads a 150MB model.
        // Found books listen whatever this says (see TranscriptionPolicy).
        public bool TranscriptionEnabled { get; set; } = false;

        // Which whisper model to transcribe with. "base.en" hears chapter announcements
        // well and runs at many times real time on a CPU; "small.en" is more accurate
        // and about three times slower.
        public string TranscriptionModel { get; set; } = "base.en";

        // Whether every newly scanned book is listened to for its spoken credits and
        // judged against its record. Needs transcription; a minute of CPU per book.
        public bool AudioAuditOnImport { get; set; } = false;

        // Whether books are probed for what their recording sounds like — hiss, muffle,
        // codec damage. Seconds of CPU per book, nothing written to any file; on by
        // default because without it the audio column of the health score stays unknown.
        // Covers both the probe at scan time and the one-time sweep of the existing
        // library.
        public bool AudioQualityProbeEnabled { get; set; } = true;

        // How many dB darker than a natural voice (treble at -25 dB relative to speech)
        // a book must be before the generative repair is suggested. Beyond this line an
        // exciter deriving harmonics from the real signal cannot honestly reach natural;
        // measured at 25 dB, 45 of 1,225 books sat past it.
        public double GenerativeOfferThresholdDb { get; set; } = 25;

        // Whether audio repair exists on this install; while off, every repair entry
        // point refuses with a sentence saying so. On by default: nothing runs until a
        // repair is asked for, and the first request downloads the enhancement model
        // into the config volume (about 4 GB, once). A repair holds 5-10 GB of memory
        // while it runs, so an install without that to spare turns it off.
        public bool RepairEnabled { get; set; } = true;

        // What happens to the original files once a repair has been verified and
        // published. Archive by default: a repair is a lossy, opinionated rewrite, and
        // the user reviews audio changes by ear - the original is the only way back.
        // Uses the same archive location as conversion (ConversionArchivePath).
        public ConversionSourceDisposition RepairSourceDisposition { get; set; } =
            ConversionSourceDisposition.Archive;

        // Whether a repair splices the original audio back wherever the model removed a
        // lot of energy from a loud passage. The model is trained on speech and destroys
        // an orchestral score; removing something loud is the signature of that failure,
        // because real cleaning only ever removes what was quiet.
        public bool RepairMusicProtectionEnabled { get; set; } = true;

        // Whether a book arriving with broken chapters has them fixed without anyone
        // asking: once the fix is worked out after import, it is applied. Only imports;
        // books already in the library are repaired from the Actions menu.
        public bool AutoRepairChapters { get; set; } = true;

        // Whether a book arriving with damaged audio is repaired without anyone asking,
        // once the probe after import recommends it. Hours of CPU per book, and the
        // original goes wherever RepairSourceDisposition says (the archive by default).
        // Only imports, for the same reason as chapters.
        public bool AutoRepairAudio { get; set; } = true;

        // Folders scanned for complete books that are not in the library: a pack that
        // arrived with more than was asked for, a manual download, an import that was
        // left behind. Empty means every enabled download client's completed path,
        // translated through its remote path mappings.
        public List<string> FoundBooksWatchFolders { get; set; } = new();

        // How often the watch folders are scanned. Zero turns the periodic scan off;
        // the Found tab's own scan button still works.
        public int FoundBooksScanIntervalMinutes { get; set; } = 60;

        // Whether a found book that is complete, not in the library, and matched to the
        // catalogue beyond doubt (an ASIN in its tags, or title and author agreeing
        // exactly) is added without asking. Anything less certain waits for a person.
        public bool FoundBooksAutoAdd { get; set; } = false;

        // Minutes between background fetches of one missing author or series catalog
        // for Suggested. One a minute fills a library of a few hundred in a few hours
        // without ever standing in the way of a search. Zero turns it off, leaving the
        // page's own Fetch button as the only way catalogs arrive.
        public int SuggestionsBackgroundFetchIntervalMinutes { get; set; } = 1;

        // Seconds between background checks of one suggested book for a release an
        // indexer holds. Each check is one abook.link search (which reads up to 15
        // topics) and, when that finds nothing, one search of each Newznab indexer;
        // half a minute walks a few thousand suggestions in a day without leaning on any
        // of them. Zero turns it off.
        public int SuggestionReleaseCheckIntervalSeconds { get; set; } = 30;

        // Which sections each discovery tab draws from, keyed by source: the Audible
        // chart categories to follow, and the abook.link boards to watch. One column
        // rather than four because the shape is a choice per source and is edited from
        // the tab itself, not from the settings page. See DiscoverSettings for the
        // document and for the validation its ids go through before a URL is built
        // from them.
        public string DiscoverSettingsJson { get; set; } = "{}";

        // Minutes between background steps of a discovery fetch: one chart page, one
        // board page, or one metadata lookup for a forum post. Zero turns it off,
        // leaving the tabs' own Refresh and Load more buttons.
        public int DiscoverBackgroundFetchIntervalMinutes { get; set; } = 5;

        // How long a book nothing holds is left alone before the indexers are asked about
        // it again. A week is often enough for a post to appear without asking every day;
        // raise it for a quieter library, lower it to chase a book you are waiting for.
        // Clamped when read, so a stray zero cannot turn this into a checking loop.
        public int UnobtainableRecheckDays { get; set; } = 7;

        // Which of the Suggested page's lists are grabbed from without being asked, in
        // what order, and how many books one run may add. Off until someone ticks a
        // list: this spends disk and a usenet allowance, so it starts doing nothing.
        // See AutoGrabSettingsJson for the document.
        public string AutoGrabSettingsJson { get; set; } = "{}";

        // Whether the background check carries on past "a release exists" and does the
        // rest of the work a download needs — thank the post, read its payload, resolve
        // an NZB — so that adding the book is a send and nothing more.
        public bool PrepareReleasesEnabled { get; set; } = true;

        // Whether that automatic preparation may spend an NZBKing token when the free
        // indexes have nothing. Off: such a release is marked as needing the metered
        // index and left for a grab the user asks for, which may still spend one.
        // Thanking and the free indexes are unaffected either way.
        public bool PrepareReleasesMayUseMeteredIndexes { get; set; } = false;

        /// <summary>
        /// How many times an hour Listenarr may press abook.link's thank button. The
        /// button is how a post gives up its payload, so this is in effect the ceiling on
        /// grabs from that source. It is a safety rail against a loop, not a disguise:
        /// the forum ranks thanks received and never thanks given, and runs a posting bot
        /// of its own.
        /// </summary>
        public int AbookThanksPerHour { get; set; } = 60;

        // What goes into each tag and whether it may be overwritten. Null means the
        // shipped defaults, which mirror the library's own bracket convention.
        // See TagCatalog for the tags, their defaults and why each one is what it is.
        public List<TagMapping>? TagMappings { get; set; }

        // Maximum number of concurrent ffprobe processes during an unmatched scan.
        // Lower values reduce NAS/disk I/O pressure; higher values speed up large libraries.
        public int UnmatchedScanConcurrency { get; set; } = 2;

        // Whether to show completed downloads from external clients in the Activity view
        public bool ShowCompletedExternalDownloads { get; set; } = false;

        /// <summary>
        /// The books page's saved custom filters, as the JSON array the UI defines them in.
        ///
        /// Here rather than in the browser because a filter someone builds on one machine
        /// is invisible on the next one otherwise - localStorage is per-browser, so the
        /// same library showed a different set of filters on a laptop and a desktop.
        ///
        /// Stored as opaque JSON on purpose: the rule grammar belongs to the filter
        /// editor, which is free to change it without a migration here. The server never
        /// interprets it - it only has to hand back what it was given.
        /// </summary>
        public string LibraryCustomFiltersJson { get; set; } = "[]";

        // Number of days to retain action history. Zero keeps history indefinitely.
        public int HistoryRetentionDays { get; set; } = 0;

        // Failed download handling settings
        public bool FailedDownloadHandlingEnabled { get; set; } = true;
        public bool FailedDownloadAutoSearch { get; set; } = false;
        public List<string> ImportBlacklistExtensions
        {
            get
            {
                return [.. FileUtils.NormalizeExtensions(field)];
            }
            set;
        } = [];

        /// <summary>
        /// Webhook URL for sending notifications (legacy single webhook).
        /// </summary>
        public string WebhookUrl { get; set; } = string.Empty;

        /// <summary>
        /// List of enabled notification triggers (legacy).
        /// </summary>
        public List<string> EnabledNotificationTriggers { get; set; } = new() { "book-added", "book-downloading", "book-available", "book-completed" };

        /// <summary>
        /// Multiple webhooks configuration (new format).
        /// </summary>
        public List<WebhookConfiguration>? Webhooks { get; set; }

        // Optional admin credentials submitted from the UI when saving settings.
        // These are NOT mapped to the ApplicationSettings table; they are used to create/update
        // a User record in the Users table via the ConfigurationService.
        /// <summary>
        /// Admin username submitted from the UI (not persisted to the settings table).
        /// </summary>
        [NotMapped]
        public string? AdminUsername { get; set; }

        [NotMapped]
        public string? AdminPassword { get; set; }

        // Discord bot integration settings (used by external Discord bot or interactions)
        /// <summary>
        /// Enable (persisted) Discord bot integration settings. The bot process may read these settings to
        /// automatically login / register commands.
        /// </summary>
        public bool DiscordBotEnabled { get; set; } = false;

        /// <summary>
        /// Discord Application (Client) ID for registering application commands.
        /// </summary>
        public string? DiscordApplicationId { get; set; }

        /// <summary>
        /// Optional Guild ID to register commands in a single guild for faster deployment during testing.
        /// </summary>
        public string? DiscordGuildId { get; set; }

        /// <summary>
        /// Optional Channel ID to restrict bot interactions to a single channel. If set, the bot
        /// will ignore interactions from other channels unless the bot configuration allows it.
        /// </summary>
        public string? DiscordChannelId { get; set; }

        /// <summary>
        /// Bot token used by an external bot process to authenticate to Discord.
        /// NOTE: Storing tokens in the database has security implications. Consider using a secrets manager
        /// for production deployments.
        /// </summary>
        public string? DiscordBotToken { get; set; }

        /// <summary>
        /// Saved Prowlarr host/URL used by the indexer import flow.
        /// </summary>
        public string? ProwlarrUrl { get; set; }

        /// <summary>
        /// Optional saved Prowlarr port used by the indexer import flow.
        /// </summary>
        public int? ProwlarrPort { get; set; }

        /// <summary>
        /// Encrypted Prowlarr API key used by the indexer import flow.
        /// </summary>
        public string? ProwlarrApiKeyEncrypted { get; set; }

        /// <summary>
        /// Optional Prowlarr tag filter used by the indexer import flow.
        /// When set, only indexers with this tag are imported and the audiobook category filter is bypassed.
        /// </summary>
        public string? ProwlarrTagFilter { get; set; }

        /// <summary>
        /// Primary command group name (e.g. "request"). We'll create a slash command with this group and
        /// a subcommand for specific request types (e.g. "audiobook").
        /// </summary>
        public string? DiscordCommandGroupName { get; set; } = "request";

        /// <summary>
        /// Subcommand name for audiobooks (e.g. "audiobook"). Combined with the group this yields "/request audiobook".
        /// </summary>
        public string? DiscordCommandSubcommandName { get; set; } = "audiobook";

        /// <summary>
        /// Optional custom username for the Discord bot. If set, the bot will attempt to change its username.
        /// </summary>
        public string? DiscordBotUsername { get; set; }

        /// <summary>
        /// Optional avatar URL for the Discord bot. If set, the bot will attempt to change its avatar.
        /// </summary>
        public string? DiscordBotAvatar { get; set; }

        // Search settings
        /// <summary>
        /// Enable searching Amazon as part of intelligent searches.
        /// </summary>
        public bool EnableAmazonSearch { get; set; } = true;

        /// <summary>
        /// Enable searching Audible as part of intelligent searches.
        /// </summary>
        public bool EnableAudibleSearch { get; set; } = true;

        /// <summary>
        /// Enable using OpenLibrary augmentation during intelligent searches.
        /// </summary>
        public bool EnableOpenLibrarySearch { get; set; } = true;

        /// <summary>
        /// Preferred default Audible/Audible market region for Add New searches.
        /// </summary>
        public string DefaultSearchRegion { get; set; } = "us";

        /// <summary>
        /// Preferred default language filter for Add New searches.
        /// </summary>
        public string DefaultSearchLanguage { get; set; } = "english";

        /// <summary>
        /// The languages the library is read in, as a JSON array of Audible language
        /// names ("english", "german", …). Suggestions only offer books in these. Empty
        /// means "just <see cref="DefaultSearchLanguage"/>"; that set to "all" means no
        /// language filter at all.
        /// </summary>
        public string LibraryLanguagesJson { get; set; } = "[]";

        /// <summary>
        /// Words dropped from the end of a series name when it is written into a path
        /// or a tag ("Series", "Trilogy", …), as a JSON array. See
        /// <see cref="SeriesNameStyle"/>. The stored series name is never changed.
        /// </summary>
        public string SeriesNameDropWordsJson { get; set; } = SeriesNameStyle.DefaultDropWordsJson;

        /// <summary>
        /// Series names written in full even when they end in a drop word, as a JSON
        /// array — for a name that dropped would read as another series. See
        /// <see cref="SeriesNameStyle"/>.
        /// </summary>
        public string SeriesNamesKeptWholeJson { get; set; } = "[]";

        /// <summary>
        /// Author spellings to store as another, as a JSON list of
        /// <c>{"variant","canonical"}</c>. See <see cref="AuthorAliases"/>.
        /// </summary>
        public string AuthorAliasesJson { get; set; } = AuthorAliases.EmptyJson;

        /// <summary>
        /// How many narrators a folder, file or tag names before the list is cut and ends
        /// in "et al."; 0 names every narrator. See <see cref="NarratorNameStyle"/>.
        /// </summary>
        public int MaxNarratorsInNames { get; set; }

        /// <summary>
        /// File every book of a series under the author of its first book, so a series
        /// that changed hands stays in one folder and on one author page. The book's own
        /// credit still goes into <c>{Authors}</c>. See <see cref="SeriesAuthorRule"/>.
        /// </summary>
        public bool FileSeriesUnderFirstAuthor { get; set; } = true;

        /// <summary>
        /// Series whose filing author the operator has set by hand, as a JSON list of
        /// <c>{"series","author"}</c>, for the cases the rule gets wrong.
        /// </summary>
        public string SeriesAuthorOverridesJson { get; set; } = SeriesAuthorRule.EmptyOverridesJson;
    }
}

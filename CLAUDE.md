# Fork context

This is `nexalapp/Listenarr`, a fork of `Listenarrs/Listenarr` used to manage a
personal audiobook library on an unraid NAS. It tracks upstream and carries a
small number of fork-only files.

Upstream's own agent instructions live in `.github/CLAUDE.md` (security rules,
review process, formatting, test conventions, layering). **Those still apply** —
this file only adds what is specific to the fork.

## Fork-only files — never PR these upstream

| Path | Purpose |
|---|---|
| `run.sh` | Task runner for the Docker dev environment (`./run.sh dev`, `logs`, `tests`, `sync_upstream`, …) |
| `deploy/unraid/` | Compose project + docs for the NAS deployment |
| `CLAUDE.md` | This file |

Everything else should stay mergeable with upstream. When a fix is general, open
a PR against `Listenarrs/Listenarr` rather than only landing it here.

## Branches and release channels

Upstream's model, which the fork follows:

| Branch | Role | Publishes |
|---|---|---|
| `canary` | Alpha — all feature work | `:canary`, `:canary-<version>` |
| `beta` | Release candidate | `:beta`, `:beta-<version>`, `:beta-<version>-<sha>` |
| `main` | Stable — tag `v*` triggers it | `:stable`, `:latest`, `:<version>` |

Images publish to `ghcr.io/nexalapp/listenarr` (public, anonymous pull works).
Docker Hub and ATCR tags in the workflows point at upstream-owned namespaces;
their logins fail under `continue-on-error` and those push steps skip. That is
expected on the fork, not a broken build.

### Merging to canary requires a labelled PR

`canary.yml` fails on any push that is not a merged PR carrying exactly one of
`patch` / `minor` / `major`. Direct pushes to `canary` will not build. The label
drives the version bump, which is what keeps published version tags immutable —
without a bump, a re-release silently overwrites the previous tag of the same
name.

The bump itself is automated: `canary.yml` writes the new version to the csproj,
syncs `fe/package.json`, then opens, labels, and auto-merges a version-bump PR
using `secrets.GH_PAT`. The merge commit is prefixed `[skip ci]` so it does not
re-trigger.

`GH_PAT` is a fine-grained token on the repo with Contents, Pull requests, and
Issues set to read/write (labels are issue-scoped, hence Issues).

## Key decisions

**Descriptions must be embedded in the file, not written to Plex's database.**
The original problem was audiobook descriptions not appearing in Plex or
Prologue. Plex populates an album summary *only* from the MP4 `desc` atom — it
ignores ID3 entirely, and ignores `©cmt` and `TIT3` even in MP4. Writing to
Plex's database was rejected as a fix because the metadata has to survive
outside Plex.

**Deploy a built image to the NAS; never run over SMB.** The app runs on the
server itself against local disk. See `deploy/unraid/README.md`.

**Pin an immutable tag, never a rolling one.** `:canary` and `:beta` move.
`:beta-<version>-<sha>` and `:<version>` do not. The deployed tag lives in
`deploy/unraid/docker-compose.yml`; rolling back is a one-line edit.

**The library mount is read-write, as of 2026-09-01.** It started `:ro` while the
existing library had not been validated against the scanner. Conversion is what
required the change: it encodes to scratch outside the library and then publishes
the m4b back in through `FileMover`, so a read-only mount fails at the last step,
after the whole encode has been spent. Imports need it for the same reason.
Renaming can now move any of the 439G, so a naming-pattern change is no longer a
cheap experiment.

**ffmpeg is enough for M4B; m4b-tool is not needed.** Verified by inspecting the
bytes ffmpeg produces: it writes the `desc` atom (the whole reason for this fork),
`chpl` Nero chapters *and* a QuickTime chapter track, and `covr` cover art. What
m4b-tool would add — libfdk_aac, silence-based chapter splitting, an AAC remux
path — is irrelevant under a 128k cap, for one-file-per-chapter sources, and for
MP3 input. It would cost a PHP runtime plus mp4v2 in an image that has neither.

**Conversion uses ffmpeg's concat *filter*, never the concat demuxer.** The
demuxer adopts the first input's sample rate and channel layout for the whole
book — a 44.1kHz stereo chapter after a 22kHz mono one is silently downmixed,
with no error — and its output drifts from the nominal duration, walking the
chapter marks out of sync over a long book. Measured: 110ms of drift over two
files through the demuxer, 0ms through the filter.

**Conversion handles both shapes: multi-part files and one merged chaptered MP3.**
Files with no marks of their own get one chapter each, ordered naturally. Files
that already carry ID3 CHAP frames keep those marks, offset by whatever plays
before them; ffprobe reads them, so nothing is lost. The two mix freely in one
book.

The merged-MP3 case is the one that matters for this library: every one of its
375 MP3 books is a single already-chaptered file, so a converter that ignored
embedded marks would flatten all of them to one chapter. It is also the most
valuable case, because ID3 cannot carry `desc` at all and MP4 can.

**Tag values come from the naming-pattern language, not a bespoke formatter.**
The album tag has to mirror the folder name, so it uses the same templates and the
same empty-token collapse: `{SeriesBrackets} {Title}` renders
`[The Expanse 2.7] Drive` for a series book and `Drive` for a standalone, with no
conditional. `{SeriesBrackets}` reads every `AudiobookSeriesMembership`, which is
what produces the double-bracket `[Enderverse 07.5][Ender's Saga 1.1]` form the
library's cross-series books carry and the primary series alone cannot. Tag
rendering skips path sanitisation — a tag may hold a colon, a slash and, for a
blurb, paragraph breaks.

**One planner decides every tag value, for tagging and for conversion.** Two
renderings of one mapping would eventually disagree, and converting a book would
then produce different tags from enriching it. `AudiobookTagPlanner` is the only
place a tag value is decided; conversion resolves through it against an empty set
of existing tags.

**ffmpeg cannot write this library's tags. TagLib# can.** The mov muxer writes
only the keys it has standard atoms for and drops the rest silently — `SERIES`,
`SERIESPOSITION`, `ASIN` and `sort_album`, which is exactly the set the library's
files carry. `-movflags +use_metadata_tags` appears to fix that and does not: it
writes QuickTime **mdta** metadata (a `keys` table plus an index-addressed `ilst`)
rather than the iTunes `----` atoms players read, *and* it drops cover art
outright. ffprobe reads both forms back under the same names, so a round-trip
check alone cannot tell them apart — assert on the bytes. Tag writing is therefore
a byte copy plus a direct atom edit through TagLib#: audio, chapter tracks and
cover art survive by construction. Conversion still encodes with ffmpeg, because
only ffmpeg writes the chapters, then applies the tags the same way.

**Publication cannot overwrite, so a tag write removes the original first.**
`FileMover.PrepareActionForRegistrationAsync` treats an existing destination as a
resumed publication of the *same* bytes and refuses when the content differs — a
replacement is exactly that case. The rewrite is recorded against the job
*before* the original is removed, so a crash in the window leaves a durable row
naming the book's only copy; the scratch sweeper is forbidden from touching a
file in that state, and the next attempt publishes it before doing anything else.
This is only defensible because the copy is verified first: same audio, same
chapters, same cover, every written tag read back.

**Automatic tagging hangs off scan completion, next to conversion.** Both import
paths converge there. A book conversion accepts is not also offered for tagging,
because the conversion writes the tags itself. A re-run is free: the planner finds
nothing to write and no file is opened.

**An embedded title only names a chapter when it distinguishes the file.** Parts
split from one book commonly all carry the book's own title tag, and preferring
it named every chapter identically. A title shared by more than one source falls
back to the filename; a lone source keeps its own title.

**Conversion writes outside the library, then publishes through `FileMover`.**
`BackendArchitectureTests.LibraryFilesystem_HasNoListenarrScratchNamespaceProtocol`
forbids a scratch namespace in the library filesystem, and a half-written encode
beside the book is exactly what that prevents — nothing reading the directory can
tell it from a real file.

**Conversion hooks scan completion, because that is where the two import paths
converge.** The download path goes through `IDownloadImportService`; the
manual/library path does not (`ManualImportController` composes its own
dependencies). Both finish by calling `IScanQueueService.EnqueueScanAsync` with
the audiobook. Hooking file registration instead would fire once per file and put
a side effect inside the registration lease's rollback path.

**Parsing is derived from the configured naming pattern, not hardcoded.**
`PathMetadataParser` previously used a fixed regex that could never match the
folders the renamer produces under the default pattern. It now builds a matcher
from `FolderNamingPattern` (see `NamingPatternFolderMatcher`), so scanning reads
back the layout renaming writes.

**macOS is not a supported dev target here.** Scanning is broken on macOS: on
x64 the wrong `fstat` ABI misclassifies every file as a character device, and on
arm64 the platform gate exists for good reason — lifting it caused an unkillable
kernel hang. Use the Docker dev environment (`./run.sh dev`).

## Development

`./run.sh dev` starts the Docker dev environment (API + Vite with hot reload).
`./run.sh help` lists the rest. The dev library defaults to `../audiobooks-test`;
override with `LISTENARR_DEV_LIBRARY`.

Before pushing, note the hooks: pre-commit runs formatting and layering checks,
pre-push runs `dotnet format --verify-no-changes`, `vue-tsc`, and `vitest`.

New test classes must inherit `BaseTests` and carry `[Trait("Name", "<exact
class name>")]` plus a non-empty `[Trait("Category", …)]`, enforced by
`BackendArchitectureTests.TestClasses_FollowRepositoryConventions`. Existing
classes that violate this are grandfathered via `LegacyTestConventionExemptions`
— do not copy their shape.

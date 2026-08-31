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

**The library mount starts read-only.** Listenarr renames and moves files once a
root folder is configured, and the existing library has not been validated
against the scanner. Drop `:ro` only after a scan looks correct.

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

**A source file's own chapters are preserved.** ffprobe reads ID3 CHAP frames, so
a book already merged into one chaptered MP3 keeps its marks (offset by the files
before it). Converting such a book is the *most* valuable case here, because ID3
cannot carry `desc` at all.

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

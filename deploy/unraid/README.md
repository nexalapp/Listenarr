# unraid deployment

Fork-local deployment config for the NAS. Not upstreamable — the image
namespace, share paths, and PUID/PGID are specific to this install.

## Target

| | |
|---|---|
| Host | `root@10.10.10.10` (unraid, `NAS`) |
| URL | http://10.10.10.10:4545 |
| Compose project | `/boot/config/plugins/compose.manager/projects/listenarr/` |
| Config volume | `/mnt/cache/appdata/listenarr` → `/app/config` (rw) |
| Library volume | `/mnt/user/audiobooks` → `/audiobooks` (rw) |

The app listens on 4545 (`EXPOSE 4545`, `ASPNETCORE_URLS=http://*:4545`). To
move it, remap the host side of the port rather than editing `ASPNETCORE_URLS`.

Enter `/audiobooks` — the container path — when configuring the root folder in
the UI. The host path does not exist inside the container.

### Config binds the pool, not the user share

`appdata` is bound as `/mnt/cache/appdata/listenarr`, deliberately skipping
`/mnt/user`. shfs is FUSE, and on 2026-09-01 it hit its 40960 file-descriptor
limit - exhausted by an unrelated SMB session holding ~40k handles open. Every
`open()` under `/mnt/user` then failed while `stat()` kept working, so the share
listed as empty and SQLite lost its own database mid-write, corrupting a table.
The array was healthy throughout; no disk was at fault.

A SQLite database has no business behind a FUSE layer it does not need. appdata
lives only on this pool, so `/mnt/user` adds a failure mode and nothing else.
The library share still goes through `/mnt/user`, because it genuinely spans
disks.

### Deliberate deviation from the rest of the stack

Listenarr runs in gluetun's network namespace, as the other *arr containers do.
It did not until 2026-09-02: a container in that namespace cannot publish its own
port, so staying out of it kept 4545 reachable without touching gluetun.

Wiring up Prowlarr is what settled it. Prowlarr lives in that namespace and
gluetun's firewall is `-P OUTPUT DROP` with no LAN allowance, so it could not
reach Listenarr across the network at all - the application test timed out.
Joining the namespace is how every other app there is reached: Prowlarr uses
`http://localhost:4545`, exactly as it uses `localhost:7878` for Radarr.

gluetun publishes 4545 on Listenarr's behalf, so the UI stays reachable at
http://10.10.10.10:4545. Listenarr's own traffic now leaves through the VPN and
stops when the tunnel does.

## Deploying

The Compose Manager plugin reads projects from the flash drive, so the file is
copied there rather than run from the repo. The copy in this directory is
authoritative — edit here, then push:

```sh
scp deploy/unraid/docker-compose.yml \
    root@10.10.10.10:/boot/config/plugins/compose.manager/projects/listenarr/
ssh root@10.10.10.10 \
    'cd /boot/config/plugins/compose.manager/projects/listenarr && docker compose up -d'
```

The project directory also needs a `name` file containing `listenarr` for the
plugin to list it. That already exists; it only matters when recreating from
scratch.

## Choosing an image tag

`image:` must be an **immutable** tag so the server only moves when this file
changes, and so a bad deploy rolls back with a one-line edit.

| Tag | Immutable | Use |
|---|---|---|
| `:canary`, `:beta`, `:stable`, `:latest` | no | never — these move under the server |
| `:beta-<version>-<sha>` | yes | deploying straight off `beta` |
| `:<version>` | yes* | deploying a tagged release off `main` |

\* `:<version>` is only write-once because `canary.yml` bumps the version on
every labelled merge. If that bump ever stops working, re-releasing at an
unchanged version overwrites the tag and the pin becomes meaningless.

## Cutting a release to deploy

Feature work lands on `canary` via a PR labelled `patch`/`minor`/`major` (a
direct push will not build — see `CLAUDE.md`). Then:

```sh
# promote canary → beta; beta.yml publishes beta-<version>-<sha>
git checkout beta && git merge origin/canary && git push origin beta

# optional: promote beta → main and tag, for :stable/:latest/:<version>
git checkout main && git merge origin/beta && git push origin main
git tag v<version> && git push origin v<version>
```

Merge rather than `push canary:beta` — the branches diverge (each carries its
own merge commits), so a fast-forward push gets rejected.

Then update `image:` in `docker-compose.yml` and deploy as above.

`release.yml` also builds an osx-x64 artifact (`include_osx: true`), which is
unused here — macOS is not a supported runtime for this app.

## Verifying a deploy

```sh
ssh root@10.10.10.10 'docker ps --filter name=listenarr; docker logs --tail 40 listenarr'
curl -s -o /dev/null -w '%{http_code}\n' http://10.10.10.10:4545/
```

Expect the app process to run as uid 99 / gid 100 (the entrypoint remaps from
`PUID`/`PGID`), and ffprobe to self-install into `/app/config/ffmpeg/ffprobe` on
first start.

## Library and downloads mounts

The library mount is now writable, because a read-only library cannot be imported
into: every import fails at the moment it tries to place the file. Listenarr
renames and moves files once a root folder is configured, and the existing 439G
library has not been validated against the scanner's path parsing, so the
protection the `:ro` used to provide is gone.

Before the first import, run a full scan and confirm parsing looks right.
`LISTENARR_LOG_LEVEL=Debug` gives per-file reasons instead of aggregate counts;
set it back to `Information` afterward, as Debug across the full library is
verbose. Take a backup of the share first if that scan has not been eyeballed.

The downloads share is mounted at `/downloads`, which is the same path the
download client reports its completed files under. That alignment is deliberate:
Listenarr resolves import sources by the path the client gives it, so matching
the paths means no remote path mapping is needed. Mount it anywhere else and
every import fails with "No importable files found" while the file sits plainly
visible on the share.

Both are read-write. The file mover renames rather than copies when source and
destination share a volume, and a rename has to remove the original.

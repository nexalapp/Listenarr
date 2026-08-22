# unraid deployment

Fork-local deployment config for the NAS. Not upstreamable — the image
namespace, share paths, and PUID/PGID are specific to this install.

## Target

| | |
|---|---|
| Host | `root@10.10.10.10` (unraid, `NAS`) |
| URL | http://10.10.10.10:4545 |
| Compose project | `/boot/config/plugins/compose.manager/projects/listenarr/` |
| Config volume | `/mnt/user/appdata/listenarr` → `/app/config` (rw) |
| Library volume | `/mnt/user/audiobooks` → `/audiobooks` (**ro**) |

The app listens on 4545 (`EXPOSE 4545`, `ASPNETCORE_URLS=http://*:4545`). To
move it, remap the host side of the port rather than editing `ASPNETCORE_URLS`.

Enter `/audiobooks` — the container path — when configuring the root folder in
the UI. The host path does not exist inside the container.

### Deliberate deviation from the rest of the stack

The other *arr containers run `network_mode: container:gluetunvpn`. Listenarr
does not. A container on gluetun's network cannot publish its own port, so
exposing 4545 would mean editing gluetun's mappings and restarting it, which
drops every container riding on it. Revisit only when wiring up indexers.

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

## Read-only library

The library mount is read-only. Listenarr renames and moves files once a root
folder is configured, and the existing 439G library has not been validated
against the scanner's path parsing. A write attempt fails loudly rather than
damaging anything — that is the point.

Before granting write access, run a full scan and confirm parsing looks right.
`LISTENARR_LOG_LEVEL=Debug` gives per-file reasons instead of aggregate counts;
set it back to `Information` afterward, as Debug across the full library is
verbose.

Drop the `:ro` and add a `/mnt/user/downloads` mount when moving on to imports.

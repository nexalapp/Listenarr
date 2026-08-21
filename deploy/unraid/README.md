# unraid deployment

Fork-local deployment config for the NAS. Not upstreamable — the image
namespace, share paths, and PUID/PGID are specific to this install.

## Layout

The Compose Manager plugin reads projects from the flash drive, so this file is
copied there rather than run from the repo:

```
/boot/config/plugins/compose.manager/projects/listenarr/
├── docker-compose.yml   # copy of this directory's file
└── name                 # "listenarr"
```

Update with:

```sh
scp deploy/unraid/docker-compose.yml \
    root@10.10.10.10:/boot/config/plugins/compose.manager/projects/listenarr/
ssh root@10.10.10.10 \
    'cd /boot/config/plugins/compose.manager/projects/listenarr && docker compose up -d'
```

## Image tags

`image:` is pinned to a SHA-suffixed beta tag rather than the rolling `:beta`,
so the server only moves when this file changes and a bad deploy can be rolled
back by editing one line. The tags published by `.github/workflows/beta.yml` are:

```
ghcr.io/nexalapp/listenarr:beta                       # rolling
ghcr.io/nexalapp/listenarr:beta-<version>             # rolling within a version
ghcr.io/nexalapp/listenarr:beta-<version>-<short_sha> # immutable — use this
```

## Library mount

`/mnt/user/audiobooks` is mounted read-only. Listenarr renames and moves files
once a library root is configured, so the first scan of an existing 439G library
runs with the mount read-only to confirm parsing before granting write access.
Drop the `:ro` and add a `/mnt/user/downloads` mount when moving on to imports.

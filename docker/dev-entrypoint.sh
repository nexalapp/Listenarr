#!/bin/sh
# Dev entrypoint. Lives in a file rather than compose's `command:` so the nested
# quoting concurrently needs is not mangled by YAML folding.
set -e

echo "[dev] installing root dependencies"
npm install --no-audit --no-fund --silent

echo "[dev] installing frontend dependencies"
(cd /src/fe && npm install --no-audit --no-fund --silent)

echo "[dev] ffprobe: $(command -v ffprobe || echo 'MISSING')"
echo "[dev] starting API + web"

# Restart on a rude edit instead of asking. An edit hot reload cannot apply --
# adding a type, deleting one, changing a signature -- otherwise stops watch at
# "Do you want to restart your app? Yes (y) / No (n)", and nothing is attached to
# this container's stdin to answer it. The API then keeps serving the code it
# started with, which looks like a 404 on a route you just added rather than like
# a stalled reload.
export DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1

exec npx concurrently --names API,WEB --prefix-colors blue,green \
  "cd /src/listenarr.api && dotnet watch run --non-interactive --no-launch-profile --urls http://0.0.0.0:4545" \
  "cd /src/fe && npm run dev -- --host 0.0.0.0 --port 5173"
